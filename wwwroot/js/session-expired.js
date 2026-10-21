// Used by MainLayout to show "Session expired; page was refreshed." after a Blazor circuit reload.
window.checkAndClearSessionExpired = function () {
    try {
        var key = 'blazor-session-expired';
        if (sessionStorage.getItem(key)) {
            sessionStorage.removeItem(key);
            return true;
        }
    } catch (_) {}
    return false;
};
