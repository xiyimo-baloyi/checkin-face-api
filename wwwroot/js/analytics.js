/**
 * Analytics Dashboard JS Bridge
 */

window.analytics = {
    registrationChart: null,
    attendanceTrendChart: null,

    renderRegistrationChart: function (canvasId, labels, data) {
        // Destroy existing chart if any
        if (this.registrationChart) {
            this.registrationChart.destroy();
            this.registrationChart = null;
        }
        var canvas = document.getElementById(canvasId);
        if (!canvas) return;
        var ctx = canvas.getContext('2d');

        this.registrationChart = new Chart(ctx, {
            type: 'line',
            data: {
                labels: labels,
                datasets: [{
                    label: 'Registrations',
                    data: data,
                    borderColor: '#2563eb', // Royal Blue
                    backgroundColor: 'rgba(37, 99, 235, 0.15)',
                    borderWidth: 3,
                    fill: true,
                    tension: 0.4,
                    pointBackgroundColor: '#2563eb',
                    pointRadius: 4
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                scales: {
                    y: {
                        beginAtZero: true,
                        ticks: {
                            stepSize: 1
                        },
                        grid: {
                            display: true,
                            color: 'rgba(0,0,0,0.05)'
                        }
                    },
                    x: {
                        grid: {
                            display: false
                        }
                    }
                },
                plugins: {
                    legend: {
                        display: false
                    },
                    tooltip: {
                        mode: 'index',
                        intersect: false
                    }
                }
            }
        });
    },

    renderAttendanceTrendChart: function (canvasId, labels, presentData, lateData) {
        // Destroy existing chart if any
        if (this.attendanceTrendChart) {
            this.attendanceTrendChart.destroy();
            this.attendanceTrendChart = null;
        }
        var canvas = document.getElementById(canvasId);
        if (!canvas) return;
        var ctx = canvas.getContext('2d');

        this.attendanceTrendChart = new Chart(ctx, {
            type: 'line',
            data: {
                labels: labels,
                datasets: [
                    {
                        label: 'Present',
                        data: presentData,
                        borderColor: '#10b981',
                        backgroundColor: 'rgba(16, 185, 129, 0.1)',
                        borderWidth: 3,
                        fill: true,
                        tension: 0.4,
                        pointBackgroundColor: '#10b981',
                        pointRadius: 4
                    },
                    {
                        label: 'Late',
                        data: lateData,
                        borderColor: '#f59e0b',
                        backgroundColor: 'rgba(245, 158, 11, 0.1)',
                        borderWidth: 2,
                        fill: false,
                        tension: 0.4,
                        pointBackgroundColor: '#f59e0b',
                        pointRadius: 3,
                        borderDash: [5, 5]
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                scales: {
                    y: {
                        beginAtZero: true,
                        ticks: {
                            stepSize: 1
                        },
                        grid: {
                            display: true,
                            color: 'rgba(0,0,0,0.05)'
                        }
                    },
                    x: {
                        grid: {
                            display: false
                        }
                    }
                },
                plugins: {
                    legend: {
                        display: true,
                        position: 'top',
                        labels: {
                            usePointStyle: true,
                            padding: 15,
                            font: {
                                size: 12,
                                weight: 'bold'
                            }
                        }
                    },
                    tooltip: {
                        mode: 'index',
                        intersect: false,
                        backgroundColor: 'rgba(0, 0, 0, 0.8)',
                        titleColor: '#fff',
                        bodyColor: '#fff',
                        borderColor: '#ddd',
                        borderWidth: 1,
                        padding: 12,
                        displayColors: true
                    }
                },
                interaction: {
                    mode: 'nearest',
                    axis: 'x',
                    intersect: false
                }
            }
        });
    },

    notificationHandler: null,
    registerNotificationHandler: function (handler) {
        this.notificationHandler = handler;
    },
    dispatchNotificationUpdate: function () {
        if (this.notificationHandler) {
            this.notificationHandler.invokeMethodAsync("UpdateUnreadCount");
        }
    }
};

