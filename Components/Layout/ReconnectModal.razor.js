// ── Reconnect Modal Handling ──
const reconnectModal = document.getElementById("components-reconnect-modal");
if (reconnectModal) {
    reconnectModal.addEventListener("components-reconnect-state-changed", handleReconnectStateChanged);
}

const retryButton = document.getElementById("components-reconnect-button");
if (retryButton) {
    retryButton.addEventListener("click", retry);
}

const resumeButton = document.getElementById("components-resume-button");
if (resumeButton) {
    resumeButton.addEventListener("click", resume);
}

// Track how many consecutive retry failures we've had
let retryFailCount = 0;
const MAX_AUTO_RETRIES = 3;

function handleReconnectStateChanged(event) {
    if (event.detail.state === "show") {
        reconnectModal.showModal();
        retryFailCount = 0;
    } else if (event.detail.state === "hide") {
        reconnectModal.close();
        retryFailCount = 0;
    } else if (event.detail.state === "failed") {
        retryFailCount++;
        if (retryFailCount >= MAX_AUTO_RETRIES) {
            // Circuit is gone on the server — auto-reload instead of showing a dead button
            console.warn('[Blazor] Circuit reconnect failed after retries, reloading page...');
            try { sessionStorage.setItem('blazor-session-expired', '1'); } catch (_) {}
            location.reload();
        } else {
            document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
        }
    } else if (event.detail.state === "rejected") {
        try { sessionStorage.setItem('blazor-session-expired', '1'); } catch (_) {}
        location.reload();
    }
}

async function retry() {
    document.removeEventListener("visibilitychange", retryWhenDocumentBecomesVisible);

    try {
        // Reconnect will asynchronously return:
        // - true to mean success
        // - false to mean we reached the server, but it rejected the connection (e.g., unknown circuit ID)
        // - exception to mean we didn't reach the server (this can be sync or async)
        const successful = await Blazor.reconnect();
        if (!successful) {
            // We have been able to reach the server, but the circuit is no longer available.
            // We'll reload the page so the user can continue using the app as quickly as possible.
            const resumeSuccessful = await Blazor.resumeCircuit();
            if (!resumeSuccessful) {
                try { sessionStorage.setItem('blazor-session-expired', '1'); } catch (_) {}
                location.reload();
            } else {
                reconnectModal.close();
            }
        }
    } catch (err) {
        // We got an exception, server is currently unavailable
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    }
}

async function resume() {
    try {
        const successful = await Blazor.resumeCircuit();
        if (!successful) {
            try { sessionStorage.setItem('blazor-session-expired', '1'); } catch (_) {}
            location.reload();
        }
    } catch {
        try { sessionStorage.setItem('blazor-session-expired', '1'); } catch (_) {}
        location.reload();
    }
}

async function retryWhenDocumentBecomesVisible() {
    if (document.visibilityState === "visible") {
        await retry();
    }
}

// ── Stale Tab Detection ──
// When a user leaves the tab open overnight and returns the next day,
// the circuit is dead but no reconnect modal may have fired.
// This detects that scenario and forces a reload.
let lastActiveTimestamp = Date.now();

// Update the timestamp periodically while the page is active
setInterval(() => {
    if (document.visibilityState === "visible") {
        lastActiveTimestamp = Date.now();
    }
}, 30_000); // every 30 seconds

document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "visible") {
        const idleDurationMs = Date.now() - lastActiveTimestamp;
        const TWO_MINUTES = 2 * 60 * 1000;

        if (idleDurationMs > TWO_MINUTES) {
            console.info(`[Blazor] Tab was idle for ${Math.round(idleDurationMs / 1000)}s — checking circuit health...`);

            // Attempt a reconnect; if the circuit is dead, reload
            if (typeof Blazor !== 'undefined' && Blazor.reconnect) {
                Blazor.reconnect().then(success => {
                    if (!success) {
                        console.warn('[Blazor] Circuit is dead after idle, reloading...');
                        try { sessionStorage.setItem('blazor-session-expired', '1'); } catch (_) {}
                        location.reload();
                    } else {
                        console.info('[Blazor] Circuit reconnected after idle.');
                        lastActiveTimestamp = Date.now();
                    }
                }).catch(() => {
                    console.warn('[Blazor] Server unreachable after idle, reloading...');
                    try { sessionStorage.setItem('blazor-session-expired', '1'); } catch (_) {}
                    location.reload();
                });
            }
        } else {
            lastActiveTimestamp = Date.now();
        }
    }
});
