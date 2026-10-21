/**
 * Map interop for the African Ideas Attendance admin views.
 *
 * Exposes `window.attendanceMap` used by Components/Pages/Admin/AttendanceMap.razor
 * to plot branches (with geofence circles) and recent clock-in / clock-out events.
 *
 * Depends on Leaflet (loaded globally from Components/App.razor).
 */
(function () {
    "use strict";

    const ns = (window.attendanceMap = window.attendanceMap || {});

    // Keeps one Leaflet map instance per container id.
    const instances = {};

    function ensureLeaflet() {
        if (typeof window.L === "undefined") {
            console.warn("[attendanceMap] Leaflet (L) is not loaded.");
            return false;
        }
        return true;
    }

    function getInstance(containerId) {
        return instances[containerId];
    }

    function clearLayer(state, key) {
        if (state[key]) {
            state.map.removeLayer(state[key]);
        }
        state[key] = window.L.layerGroup().addTo(state.map);
    }

    ns.initializeMap = function (containerId, lat, lon, zoom) {
        if (!ensureLeaflet()) return;

        const container = document.getElementById(containerId);
        if (!container) {
            console.warn("[attendanceMap] container not found: " + containerId);
            return;
        }

        const safeLat = Number.isFinite(lat) ? lat : -29.0;
        const safeLon = Number.isFinite(lon) ? lon : 24.0;
        const safeZoom = Number.isFinite(zoom) && zoom > 0 ? zoom : 13;

        let state = instances[containerId];

        if (state) {
            state.map.setView([safeLat, safeLon], safeZoom);
            return;
        }

        const map = window.L.map(containerId, {
            zoomControl: true,
            attributionControl: true,
        }).setView([safeLat, safeLon], safeZoom);

        window.L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
            maxZoom: 19,
            attribution:
                '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
        }).addTo(map);

        state = {
            map,
            branchLayer: window.L.layerGroup().addTo(map),
            attendanceLayer: window.L.layerGroup().addTo(map),
        };
        instances[containerId] = state;
    };

    ns.setBranches = function (containerId, branches) {
        if (!ensureLeaflet()) return;
        const state = getInstance(containerId);
        if (!state || !Array.isArray(branches)) return;

        clearLayer(state, "branchLayer");

        branches.forEach((b) => {
            const lat = Number(b.latitude ?? b.Latitude);
            const lon = Number(b.longitude ?? b.Longitude);
            const radius = Number(b.geofenceRadius ?? b.GeofenceRadius ?? 150);
            const name = b.name ?? b.Name ?? "Branch";

            if (!Number.isFinite(lat) || !Number.isFinite(lon)) return;

            window.L.circle([lat, lon], {
                radius: radius,
                color: "#2563eb",
                weight: 1,
                fillColor: "#2563eb",
                fillOpacity: 0.08,
            })
                .addTo(state.branchLayer)
                .bindPopup(
                    `<strong>${escapeHtml(name)}</strong><br/><small>Geofence: ${radius}m</small>`
                );

            window.L.marker([lat, lon], { title: name })
                .addTo(state.branchLayer)
                .bindPopup(`<strong>${escapeHtml(name)}</strong>`);
        });
    };

    ns.setAttendanceMarkers = function (containerId, logs) {
        if (!ensureLeaflet()) return;
        const state = getInstance(containerId);
        if (!state || !Array.isArray(logs)) return;

        clearLayer(state, "attendanceLayer");

        logs.forEach((log) => {
            const lat = Number(log.latitude ?? log.Latitude);
            const lon = Number(log.longitude ?? log.Longitude);
            if (!Number.isFinite(lat) || !Number.isFinite(lon)) return;

            const isOut = Boolean(log.isOutOfArea ?? log.IsOutOfArea);
            const employeeName =
                log.employeeName ?? log.EmployeeName ?? log.employeeId ?? log.EmployeeId ?? "Employee";
            const clockIn = log.clockInTime ?? log.ClockInTime;
            const clockOut = log.clockOutTime ?? log.ClockOutTime;
            const status = log.status ?? log.Status ?? (clockOut ? "Clock-Out" : "Clock-In");

            const color = isOut ? "#dc2626" : "#16a34a";
            const marker = window.L.circleMarker([lat, lon], {
                radius: 8,
                color: "#ffffff",
                weight: 2,
                fillColor: color,
                fillOpacity: 0.9,
            }).addTo(state.attendanceLayer);

            const time = clockOut ?? clockIn;
            const timeStr = time ? new Date(time).toLocaleString() : "";
            const mapsLink = `https://www.google.com/maps?q=${lat},${lon}`;

            marker.bindPopup(
                `<div style="min-width:180px">
                    <strong>${escapeHtml(String(employeeName))}</strong><br/>
                    <span style="color:${color};font-weight:600">${escapeHtml(String(status))}</span><br/>
                    <small>${escapeHtml(timeStr)}</small><br/>
                    <small>${lat.toFixed(5)}, ${lon.toFixed(5)}</small><br/>
                    <a href="${mapsLink}" target="_blank" rel="noopener">Open in Google Maps</a>
                </div>`
            );
        });
    };

    function escapeHtml(value) {
        return String(value ?? "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }
})();
