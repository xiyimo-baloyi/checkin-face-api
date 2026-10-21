using Africanideasfaceapi.Data;
using Africanideasfaceapi.DTOs;
using Africanideasfaceapi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace Africanideasfaceapi.Controllers;

[ApiController]
[Route("api/workers")]
[Authorize(Roles = "Admin")]
public class WorkersController : ControllerBase
{
    private readonly AppDbContext _context;

    public WorkersController(
        AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Get all workers
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<WorkerDto>>> GetWorkers(
        [FromQuery] string? search = null,
        [FromQuery] string? registrationMethod = null,
        [FromQuery] bool? idCardVerified = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _context.Workers.Where(p => p.IsActive).AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(p => 
                p.Name.Contains(search) || 
                p.Email.Contains(search) || 
                p.IdNumber.Contains(search));
        }

        if (!string.IsNullOrEmpty(registrationMethod))
        {
            query = query.Where(p => p.RegistrationMethod == registrationMethod);
        }

        if (idCardVerified.HasValue)
        {
            query = query.Where(p => p.IdCardVerified == idCardVerified.Value);
        }

        var totalCount = await query.CountAsync();
        var workersList = await query
            .OrderByDescending(p => p.RegisteredDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
            
        var workers = workersList.Select(p => WorkerDto.FromWorker(p)).ToList();

        Response.Headers["X-Total-Count"] = totalCount.ToString();
        Response.Headers["X-Page"] = page.ToString();
        Response.Headers["X-Page-Size"] = pageSize.ToString();

        return Ok(workers);
    }

    /// <summary>
    /// Get worker by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<WorkerDto>> GetWorker(string id)
    {
        var worker = await _context.Workers
            .FirstOrDefaultAsync(p => p.Id == id && p.IsActive);

        if (worker == null)
        {
            return NotFound();
        }

        var dto = WorkerDto.FromWorker(worker);

        return Ok(dto);
    }

    /// <summary>
    /// Search workers by name, email, or ID number
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<List<WorkerDto>>> SearchWorkers([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest("Search query is required");
        }

        var workersList = await _context.Workers
            .Where(p => p.IsActive && (
                p.Name.Contains(q) ||
                p.Email.Contains(q) ||
                p.IdNumber.Contains(q)
            ))
            .Take(20)
            .ToListAsync();
            
        var workers = workersList.Select(p => WorkerDto.FromWorker(p)).ToList();

        return Ok(workers);
    }
}
