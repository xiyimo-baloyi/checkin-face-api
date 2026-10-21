using Africanideasfaceapi.Services.Interfaces;
using Africanideasfaceapi.Helpers;
using Africanideasfaceapi.Models;
using Africanideasfaceapi.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ClosedXML.Excel;

namespace Africanideasfaceapi.Services;

public class ReportService : IReportService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IAnalyticsService _analyticsService;
    private readonly ILogger<ReportService> _logger;

    public ReportService(IDbContextFactory<AppDbContext> factory, IAnalyticsService analyticsService, ILogger<ReportService> logger)
    {
        _factory = factory;
        _analyticsService = analyticsService;
        _logger = logger;
        
        // Configure QuestPDF license (Community license for non-commercial use)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    #region PDF Reports

    public async Task<byte[]> GenerateAttendancePdfAsync(DateTime startDate, DateTime endDate)
    {
        using var context = await _factory.CreateDbContextAsync();
        var attendanceRecords = await context.Attendance
            .AsNoTracking()
            .Where(a => a.ClockInTime >= startDate && a.ClockInTime <= endDate)
            .OrderByDescending(a => a.ClockInTime)
            .ToListAsync();

        // Fetch all users for lookup
        var userIds = attendanceRecords.Select(a => a.EmployeeId).Distinct().ToList();
        var users = await context.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header()
                    .Text("Attendance Report")
                    .SemiBold().FontSize(20).FontColor(Colors.Blue.Medium);

                page.Content()
                    .PaddingVertical(1, Unit.Centimetre)
                    .Column(column =>
                    {
                        column.Spacing(10);

                        column.Item().Text($"Period: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}")
                            .FontSize(12).FontColor(Colors.Grey.Darken2);

                        column.Item().Text($"Total Records: {attendanceRecords.Count}")
                            .FontSize(11).SemiBold();

                        column.Item().PaddingTop(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(1.5f);
                                columns.RelativeColumn(1.5f);
                                columns.RelativeColumn(2.5f);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyle).Text("Employee").SemiBold();
                                header.Cell().Element(CellStyle).Text("Clock In").SemiBold();
                                header.Cell().Element(CellStyle).Text("Clock Out").SemiBold();
                                header.Cell().Element(CellStyle).Text("Hours").SemiBold();
                                header.Cell().Element(CellStyle).Text("Status").SemiBold();
                                header.Cell().Element(CellStyle).Text("Location (GPS)").SemiBold();

                                static IContainer CellStyle(IContainer container)
                                {
                                    return container.DefaultTextStyle(x => x.SemiBold())
                                        .PaddingVertical(5).BorderBottom(1).BorderColor(Colors.Grey.Lighten2);
                                }
                            });

                            foreach (var record in attendanceRecords)
                            {
                                var user = users.GetValueOrDefault(record.EmployeeId);
                                var isLate = record.Status == "Late";
                                
                                table.Cell().Element(CellStyle).Text(user != null ? $"{user.Name} {user.Surname}" : record.EmployeeId);
                                table.Cell().Element(CellStyle).Text(record.ClockInTime?.ToString("yyyy-MM-dd HH:mm") ?? "N/A");
                                table.Cell().Element(CellStyle).Text(record.ClockOutTime?.ToString("yyyy-MM-dd HH:mm") ?? "N/A");
                                table.Cell().Element(CellStyle).Text(record.TotalHours.GetValueOrDefault().ToString("F2"));
                                table.Cell().Element(CellStyle).Text(record.Status ?? "Present")
                                    .FontColor(isLate ? Colors.Red.Medium : Colors.Green.Medium);
                                
                                var locStr = (record.Latitude.HasValue && record.Longitude.HasValue) 
                                    ? $"{record.Latitude.Value:F4}, {record.Longitude.Value:F4}" 
                                    : "No GPS";
                                table.Cell().Element(CellStyle).Text(locStr);

                                static IContainer CellStyle(IContainer container)
                                {
                                    return container.BorderBottom(1).BorderColor(Colors.Grey.Lighten3)
                                        .PaddingVertical(5);
                                }
                            }
                        });
                    });

                page.Footer()
                    .AlignCenter()
                    .Text(x =>
                    {
                        x.Span("Generated on ");
                        x.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).SemiBold();
                        x.Span(" | African Ideas Attendance System");
                    });
            });
        });

        return document.GeneratePdf();
    }

    public async Task<byte[]> GenerateAuditLogPdfAsync(DateTime startDate, DateTime endDate)
    {
        using var context = await _factory.CreateDbContextAsync();
        var auditLogs = await context.AuditLogs
            .AsNoTracking()
            .Where(a => a.Timestamp >= startDate && a.Timestamp <= endDate)
            .OrderByDescending(a => a.Timestamp)
            .Take(500)
            .ToListAsync();

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(2, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header()
                    .Text("Biometric Audit Log")
                    .SemiBold().FontSize(20).FontColor(Colors.Blue.Medium);

                page.Content()
                    .PaddingVertical(1, Unit.Centimetre)
                    .Column(column =>
                    {
                        column.Spacing(10);

                        column.Item().Text($"Period: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}")
                            .FontSize(11).FontColor(Colors.Grey.Darken2);

                        column.Item().Text($"Total Events: {auditLogs.Count}")
                            .FontSize(10).SemiBold();

                        column.Item().PaddingTop(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyle).Text("Timestamp").SemiBold();
                                header.Cell().Element(CellStyle).Text("User").SemiBold();
                                header.Cell().Element(CellStyle).Text("Action").SemiBold();
                                header.Cell().Element(CellStyle).Text("Result").SemiBold();
                                header.Cell().Element(CellStyle).Text("IP Address").SemiBold();

                                static IContainer CellStyle(IContainer container)
                                {
                                    return container.DefaultTextStyle(x => x.SemiBold())
                                        .PaddingVertical(5).BorderBottom(1).BorderColor(Colors.Grey.Lighten2);
                                }
                            });

                            foreach (var log in auditLogs)
                            {
                                table.Cell().Element(CellStyle).Text(log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
                                table.Cell().Element(CellStyle).Text(log.Email ?? "System");
                                table.Cell().Element(CellStyle).Text(log.ActionType);
                                table.Cell().Element(CellStyle).Text(log.Result);
                                table.Cell().Element(CellStyle).Text(log.IpAddress ?? "N/A");

                                static IContainer CellStyle(IContainer container)
                                {
                                    return container.BorderBottom(1).BorderColor(Colors.Grey.Lighten3)
                                        .PaddingVertical(4);
                                }
                            }
                        });
                    });

                page.Footer()
                    .AlignCenter()
                    .Text(x =>
                    {
                        x.Span("Generated on ");
                        x.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).SemiBold();
                        x.Span(" | African Ideas Attendance System");
                    });
            });
        });

        return document.GeneratePdf();
    }

    #endregion

    #region Excel Reports

    public async Task<byte[]> GenerateAttendanceExcelAsync(DateTime startDate, DateTime endDate)
    {
        using var context = await _factory.CreateDbContextAsync();
        var attendanceRecords = await context.Attendance
            .AsNoTracking()
            .Where(a => a.ClockInTime >= startDate && a.ClockInTime <= endDate)
            .OrderByDescending(a => a.ClockInTime)
            .ToListAsync();

        // Fetch all users for lookup
        var userIds = attendanceRecords.Select(a => a.EmployeeId).Distinct().ToList();
        var users = await context.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Attendance Report");

        // Header
        worksheet.Cell(1, 1).Value = "Attendance Report";
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 16;

        worksheet.Cell(2, 1).Value = $"Period: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}";
        worksheet.Cell(3, 1).Value = $"Total Records: {attendanceRecords.Count}";

        // Column headers
        var headerRow = 5;
        worksheet.Cell(headerRow, 1).Value = "Employee";
        worksheet.Cell(headerRow, 2).Value = "Email";
        worksheet.Cell(headerRow, 3).Value = "Clock In";
        worksheet.Cell(headerRow, 4).Value = "Clock Out";
        worksheet.Cell(headerRow, 5).Value = "Total Hours";
        worksheet.Cell(headerRow, 6).Value = "Status";
        worksheet.Cell(headerRow, 7).Value = "Out of Area";
        worksheet.Cell(headerRow, 8).Value = "Latitude";
        worksheet.Cell(headerRow, 9).Value = "Longitude";

        worksheet.Range(headerRow, 1, headerRow, 9).Style.Font.Bold = true;
        worksheet.Range(headerRow, 1, headerRow, 9).Style.Fill.BackgroundColor = XLColor.LightBlue;

        // Data
        var currentRow = headerRow + 1;
        foreach (var record in attendanceRecords)
        {
            var user = users.GetValueOrDefault(record.EmployeeId);
            var isLate = record.Status == "Late";
            
            worksheet.Cell(currentRow, 1).Value = user != null ? $"{user.Name} {user.Surname}" : record.EmployeeId;
            worksheet.Cell(currentRow, 2).Value = user?.Email ?? "";
            worksheet.Cell(currentRow, 3).Value = record.ClockInTime?.ToString("yyyy-MM-dd HH:mm") ?? "N/A";
            worksheet.Cell(currentRow, 4).Value = record.ClockOutTime?.ToString("yyyy-MM-dd HH:mm") ?? "N/A";
            worksheet.Cell(currentRow, 5).Value = record.TotalHours.GetValueOrDefault().ToString("F2");
            worksheet.Cell(currentRow, 6).Value = record.Status ?? "Present";
            worksheet.Cell(currentRow, 7).Value = record.IsOutOfArea ? "Yes" : "No";
            worksheet.Cell(currentRow, 8).Value = record.Latitude?.ToString() ?? "";
            worksheet.Cell(currentRow, 9).Value = record.Longitude?.ToString() ?? "";

            if (isLate)
            {
                worksheet.Cell(currentRow, 6).Style.Font.FontColor = XLColor.Red;
            }

            currentRow++;
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<byte[]> GenerateUserDirectoryExcelAsync()
    {
        using var context = await _factory.CreateDbContextAsync();
        var users = await context.Users
            .AsNoTracking()
            .Where(u => u.IsActive == true)
            .OrderBy(u => u.Name)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("User Directory");

        // Header
        worksheet.Cell(1, 1).Value = "User Directory";
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 16;

        worksheet.Cell(2, 1).Value = $"Total Users: {users.Count}";
        worksheet.Cell(3, 1).Value = $"Generated: {TimeHelper.GetNow():yyyy-MM-dd HH:mm}";

        // Column headers
        var headerRow = 5;
        worksheet.Cell(headerRow, 1).Value = "Name";
        worksheet.Cell(headerRow, 2).Value = "Surname";
        worksheet.Cell(headerRow, 3).Value = "Email";
        worksheet.Cell(headerRow, 4).Value = "Role";
        worksheet.Cell(headerRow, 5).Value = "Status";
        worksheet.Cell(headerRow, 6).Value = "Registration Date";
        worksheet.Cell(headerRow, 7).Value = "Branch";

        worksheet.Range(headerRow, 1, headerRow, 7).Style.Font.Bold = true;
        worksheet.Range(headerRow, 1, headerRow, 7).Style.Fill.BackgroundColor = XLColor.LightGreen;

        // Data
        var currentRow = headerRow + 1;
        foreach (var user in users)
        {
            worksheet.Cell(currentRow, 1).Value = user.Name;
            worksheet.Cell(currentRow, 2).Value = user.Surname;
            worksheet.Cell(currentRow, 3).Value = user.Email;
            worksheet.Cell(currentRow, 4).Value = user.Role;
            worksheet.Cell(currentRow, 5).Value = user.Status;
            worksheet.Cell(currentRow, 6).Value = TimeHelper.GetNow().ToString("yyyy-MM-dd");
            worksheet.Cell(currentRow, 7).Value = user.BranchId ?? "N/A";

            currentRow++;
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<byte[]> GenerateAccessLogPdfAsync(DateTime startDate, DateTime endDate)
    {
        using var context = await _factory.CreateDbContextAsync();
        var auditLogs = await context.AuditLogs
            .AsNoTracking()
            .Where(a => a.ActionType == "Login" && a.Timestamp >= startDate && a.Timestamp <= endDate)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync();

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("Access & Identity Report").SemiBold().FontSize(18).FontColor(Colors.Blue.Medium);
                        col.Item().Text($"{startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}").FontSize(10).FontColor(Colors.Grey.Medium);
                    });
                });

                page.Content().PaddingVertical(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(3);
                    });

                    table.Header(header =>
                    {
                        header.Cell().BorderBottom(1).Padding(5).Text("Timestamp").SemiBold();
                        header.Cell().BorderBottom(1).Padding(5).Text("Identity").SemiBold();
                        header.Cell().BorderBottom(1).Padding(5).Text("Status").SemiBold();
                        header.Cell().BorderBottom(1).Padding(5).Text("Reason").SemiBold();
                    });

                    foreach (var log in auditLogs)
                    {
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(log.Email ?? "Unknown");
                        
                        var isSuccess = log.Result == "Success";
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(log.Result)
                            .FontColor(isSuccess ? Colors.Green.Medium : Colors.Red.Medium);
                            
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(log.ErrorMessage ?? "N/A");
                    }
                });

                page.Footer().AlignCenter().Text(x => {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                });
            });
        });

        return document.GeneratePdf();
    }

    public async Task<byte[]> GenerateAccessLogExcelAsync(DateTime startDate, DateTime endDate)
    {
        using var context = await _factory.CreateDbContextAsync();
        var auditLogs = await context.AuditLogs
            .AsNoTracking()
            .Where(a => a.ActionType == "Login" && a.Timestamp >= startDate && a.Timestamp <= endDate)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Access Logs");

        // Headers
        worksheet.Cell(1, 1).Value = "Timestamp";
        worksheet.Cell(1, 2).Value = "User ID";
        worksheet.Cell(1, 3).Value = "Email";
        worksheet.Cell(1, 4).Value = "Status";
        worksheet.Cell(1, 5).Value = "Reason / Error";
        worksheet.Cell(1, 6).Value = "IP Address";

        worksheet.Range(1, 1, 1, 6).Style.Font.Bold = true;
        worksheet.Range(1, 1, 1, 6).Style.Fill.BackgroundColor = XLColor.LightBlue;

        var row = 2;
        foreach (var log in auditLogs)
        {
            worksheet.Cell(row, 1).Value = log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
            worksheet.Cell(row, 2).Value = log.UserId ?? "N/A";
            worksheet.Cell(row, 3).Value = log.Email ?? "N/A";
            worksheet.Cell(row, 4).Value = log.Result;
            worksheet.Cell(row, 5).Value = log.ErrorMessage ?? "N/A";
            worksheet.Cell(row, 6).Value = log.IpAddress ?? "N/A";
            row++;
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    #endregion
}
