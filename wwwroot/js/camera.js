export function startCamera(videoElementId, facingMode = 'user') {
    return new Promise((resolve, reject) => {
        // Function to find and validate video element
        const findVideoElement = () => {
            let video = null;

            // If videoElementId is provided as a string (ID), use it
            if (typeof videoElementId === 'string') {
                video = document.getElementById(videoElementId);
            }
            // If it's an ElementReference object, try to extract ID or find element
            else if (videoElementId && typeof videoElementId === 'object') {
                // Check if it has an id property
                if (videoElementId.id) {
                    video = document.getElementById(videoElementId.id);
                }
                // If ElementReference is resolved to DOM element, use it directly
                else if (typeof videoElementId.addEventListener === 'function') {
                    video = videoElementId;
                }
            }

            // Fallback: try common IDs
            if (!video) {
                video = document.getElementById('video-camera') ||
                    document.getElementById('video-camera-login') ||
                    document.querySelector('video');
            }

            return video;
        };

        // Try to find video element, with retries if not found
        let retries = 0;
        const maxRetries = 20; // Try for 2 seconds (20 * 100ms)
        const retryInterval = 100; // 100ms

        const tryFindVideo = () => {
            const video = findVideoElement();

            if (!video) {
                retries++;
                if (retries < maxRetries) {
                    setTimeout(tryFindVideo, retryInterval);
                    return;
                } else {
                    reject(new Error('Video element not found after waiting. Please ensure the video element is rendered with ID: ' + (typeof videoElementId === 'string' ? videoElementId : 'video-camera or video-camera-login')));
                    return;
                }
            }

            // Verify it's a video element
            if (typeof video.addEventListener !== 'function') {
                retries++;
                if (retries < maxRetries) {
                    setTimeout(tryFindVideo, retryInterval);
                    return;
                } else {
                    reject(new Error('Element found but addEventListener is not available. Element type: ' + (video.tagName || 'unknown')));
                    return;
                }
            }

            if (!video.tagName || video.tagName.toLowerCase() !== 'video') {
                retries++;
                if (retries < maxRetries) {
                    setTimeout(tryFindVideo, retryInterval);
                    return;
                } else {
                    reject(new Error('Element found but is not a video element. Found: ' + video.tagName));
                    return;
                }
            }

            // Element found and valid, proceed with camera initialization
            initializeCamera(video, facingMode, resolve, reject);
        };

        // Start trying to find the video element
        tryFindVideo();
    });
}

function initializeCamera(video, facingMode, resolve, reject) {
    // Stop any existing tracks before starting a new one
    if (video.srcObject) {
        const tracks = video.srcObject.getTracks();
        tracks.forEach(track => track.stop());
        video.srcObject = null;
    }

    // Add timeout for camera access (30 seconds)
    const timeout = setTimeout(() => {
        reject(new Error('Camera access timeout. Please ensure your camera is available and permissions are granted.'));
    }, 30000);

    navigator.mediaDevices.getUserMedia({
        video: {
            facingMode: facingMode,
            // Lower resolution for faster capture and upload
            width: { ideal: 640 },
            height: { ideal: 480 }
        }
    })
        .then(stream => {
            try {
                video.srcObject = stream;

                // Wait for video metadata to load before resolving
                const handleLoadedMetadata = () => {
                    clearTimeout(timeout);
                    video.play()
                        .then(() => {
                            // Wait a bit longer to ensure video is fully ready
                            setTimeout(() => {
                                if (video.readyState >= 2 && video.videoWidth > 0) {
                                    resolve(true);
                                } else {
                                    // Try one more time
                                    setTimeout(() => {
                                        if (video.readyState >= 2) {
                                            resolve(true);
                                        } else {
                                            reject(new Error('Video stream not ready'));
                                        }
                                    }, 200);
                                }
                            }, 200);
                        })
                        .catch(err => {
                            clearTimeout(timeout);
                            console.error('Error playing video:', err);
                            reject(err);
                        });
                };

                // Remove previous event listeners
                if (video.onloadedmetadata) {
                    video.onloadedmetadata = null;
                }

                // Check if metadata is already loaded
                if (video.readyState >= 1) {
                    handleLoadedMetadata();
                } else {
                    // Use addEventListener if available, otherwise use onloadedmetadata
                    if (typeof video.addEventListener === 'function') {
                        video.addEventListener('loadedmetadata', handleLoadedMetadata, { once: true });
                    } else {
                        video.onloadedmetadata = handleLoadedMetadata;
                    }

                    // Fallback timeout if metadata never loads
                    setTimeout(() => {
                        if (video.readyState < 1) {
                            clearTimeout(timeout);
                            reject(new Error('Video metadata loading timeout'));
                        }
                    }, 5000);
                }
            } catch (err) {
                clearTimeout(timeout);
                console.error('Error setting up video stream:', err);
                reject(err);
            }
        })
        .catch(err => {
            clearTimeout(timeout);
            console.error('Error accessing camera:', err);

            let errorMessage = 'Error accessing camera. ';
            if (err.name === 'NotAllowedError' || err.name === 'PermissionDeniedError') {
                errorMessage += 'Camera permission denied. Please grant camera access and try again.';
            } else if (err.name === 'NotFoundError' || err.name === 'DevicesNotFoundError') {
                errorMessage += 'No camera found. Please ensure a camera is connected.';
            } else if (err.name === 'NotReadableError' || err.name === 'TrackStartError') {
                errorMessage += 'Camera is already in use by another application.';
            } else {
                errorMessage += err.message || 'Unknown error occurred.';
            }

            reject(new Error(errorMessage));
        });
}

export function stopCamera(videoElementId) {
    // Try to find the video element
    let video;

    if (typeof videoElementId === 'string') {
        video = document.getElementById(videoElementId);
    } else if (videoElementId && typeof videoElementId.srcObject !== 'undefined') {
        video = videoElementId;
    } else {
        video = document.getElementById('video-camera') ||
            document.getElementById('video-camera-login') ||
            document.querySelector('video');
    }

    if (video && video.srcObject) {
        const tracks = video.srcObject.getTracks();
        tracks.forEach(track => track.stop());
        video.srcObject = null;
    }
}

export function capturePhoto(videoElementId, canvasElementId) {
    console.log('capturePhoto called', { videoElementId, canvasElementId });
    return new Promise((resolve, reject) => {
        let retries = 0;
        const maxRetries = 20; // Increased retries
        const retryInterval = 50; // Decreased interval for faster response

        const tryCapture = () => {
            console.log('tryCapture attempt', retries + 1);
            try {
                // Get video element by ID
                let video;
                if (typeof videoElementId === 'string') {
                    video = document.getElementById(videoElementId);
                } else if (videoElementId && typeof videoElementId.readyState !== 'undefined') {
                    video = videoElementId;
                } else {
                    video = document.getElementById('video-camera') ||
                        document.getElementById('video-camera-login') ||
                        document.querySelector('video');
                }

                if (!video) {
                    console.warn('Video element not found, retrying...');
                    if (retries < maxRetries) {
                        retries++;
                        setTimeout(tryCapture, retryInterval);
                        return;
                    }
                    reject(new Error('Video element not found'));
                    return;
                }

                console.log('Video found', { readyState: video.readyState, videoWidth: video.videoWidth, videoHeight: video.videoHeight });

                // Check if video is ready and has dimensions
                if (!video || video.readyState < 2) {
                    console.warn('Video not ready, retrying...');
                    if (retries < maxRetries) {
                        retries++;
                        setTimeout(tryCapture, retryInterval);
                        return;
                    }
                    reject(new Error('Video is not ready. Please wait for the camera to start.'));
                    return;
                }

                // Ensure video has valid dimensions
                const videoWidth = video.videoWidth || video.clientWidth || 640;
                const videoHeight = video.videoHeight || video.clientHeight || 480;

                if (videoWidth === 0 || videoHeight === 0) {
                    console.warn('Video dimensions invalid, retrying...');
                    if (retries < maxRetries) {
                        retries++;
                        setTimeout(tryCapture, retryInterval);
                        return;
                    }
                    reject(new Error('Video dimensions are not available. Please ensure the camera is working.'));
                    return;
                }

                // Get or create canvas element
                let canvas;
                if (typeof canvasElementId === 'string') {
                    canvas = document.getElementById(canvasElementId);
                } else if (canvasElementId && typeof canvasElementId.getContext === 'function') {
                    canvas = canvasElementId;
                }

                // Create a temporary canvas if we don't have one
                if (!canvas) {
                    canvas = document.createElement('canvas');
                }

                canvas.width = videoWidth;
                canvas.height = videoHeight;

                const ctx = canvas.getContext('2d');
                if (!ctx) {
                    reject(new Error('Could not get canvas 2D context.'));
                    return;
                }

                ctx.drawImage(video, 0, 0, canvas.width, canvas.height);

                // Reduced quality from 0.7 to 0.6 to improve transmission over SignalR
                const dataUrl = canvas.toDataURL('image/jpeg', 0.8);
                console.log('Capture successful, data URL length:', dataUrl.length);
                resolve(dataUrl);
            } catch (err) {
                console.error('Error capturing photo:', err);
                reject(err);
            }
        };

        tryCapture();
    });
}

/**
 * Converts HEIC/HEIF file to JPEG data URL using heic2any
 */
export async function convertHeicToJpeg(file) {
    if (!window.heic2any) {
        // Dynamic load heic2any if not present
        await new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = 'https://cdn.jsdelivr.net/npm/heic2any@0.0.4/dist/heic2any.min.js';
            script.onload = resolve;
            script.onerror = reject;
            document.head.appendChild(script);
        });
    }

    const blob = await window.heic2any({
        blob: file,
        toType: 'image/jpeg',
        quality: 0.8
    });

    return new Promise((resolve) => {
        const reader = new FileReader();
        reader.onloadend = () => resolve(reader.result);
        reader.readAsDataURL(Array.isArray(blob) ? blob[0] : blob);
    });
}

/**
 * Converts first page of PDF to JPEG data URL using pdfjs-dist
 */
export async function convertPdfToJpeg(file) {
    if (!window.pdfjsLib) {
        await new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.min.js';
            script.onload = resolve;
            script.onerror = reject;
            document.head.appendChild(script);
        });
        window.pdfjsLib.GlobalWorkerOptions.workerSrc = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.worker.min.js';
    }

    const arrayBuffer = await file.arrayBuffer();
    const pdf = await window.pdfjsLib.getDocument({ data: arrayBuffer }).promise;
    const page = await pdf.getPage(1);

    const viewport = page.getViewport({ scale: 2.0 });
    const canvas = document.createElement('canvas');
    const context = canvas.getContext('2d');
    canvas.height = viewport.height;
    canvas.width = viewport.width;

    await page.render({ canvasContext: context, viewport: viewport }).promise;
    return canvas.toDataURL('image/jpeg', 0.8);
}
