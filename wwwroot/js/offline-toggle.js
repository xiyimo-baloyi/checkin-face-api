export function setOfflineSupport(enabled) {
    if (enabled) {
        if ('serviceWorker' in navigator) {
            navigator.serviceWorker.register('/service-worker.js')
                .then(registration => {
                    console.log('Service Worker registered with scope:', registration.scope);
                })
                .catch(error => {
                    console.error('Service Worker registration failed:', error);
                });
        }
    } else {
        if ('serviceWorker' in navigator) {
            navigator.serviceWorker.getRegistrations().then(function(registrations) {
                for(let registration of registrations) {
                    registration.unregister();
                    console.log('Service Worker unregistered');
                }
            });
            
            // Also clear caches to fully "turn off" offline mode
            caches.keys().then(function(names) {
                for (let name of names) {
                    caches.delete(name);
                    console.log('Cache deleted:', name);
                }
            });
        }
    }
}
