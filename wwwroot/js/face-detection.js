// Lightweight face detection for real-time validation
// Using MediaPipe Face Detection (AI-powered)

let faceDetector = null;
let isModelLoaded = false;
let isInitializing = false;
let _lastFaceCallback = 0;
const FACE_CALLBACK_THROTTLE_MS = 700; // Don't call .NET more than ~1.4x/sec

// Shared resources
const sharedCanvas = document.createElement('canvas');
const sharedCtx = sharedCanvas.getContext('2d', { willReadFrequently: true });
let faceDetectionInterval = null;

/**
 * Loads the MediaPipe Face Detection model if not already loaded
 */
export async function loadFaceDetectionModel() {
    if (isModelLoaded) return true;
    if (isInitializing) {
        // Wait for existing initialization
        while (isInitializing) {
            await new Promise(r => setTimeout(r, 100));
        }
        return isModelLoaded;
    }

    isInitializing = true;
    try {
        // Load the FilesetResolver and FaceDetector from MediaPipe Tasks Vision
        const vision = await import("https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@0.10.0");
        const filesetResolver = await vision.FilesetResolver.forVisionTasks(
            "https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@0.10.0/wasm"
        );
        
        faceDetector = await vision.FaceDetector.createFromOptions(filesetResolver, {
            baseOptions: {
                modelAssetPath: `https://storage.googleapis.com/mediapipe-models/face_detector/blaze_face_short_range/float16/1/blaze_face_short_range.tflite`,
                delegate: "GPU"
            },
            runningMode: "IMAGE"
        });

        isModelLoaded = true;
        console.log('[FaceDetection] MediaPipe Model Loaded');
        return true;
    } catch (error) {
        console.error('[FaceDetection] Error loading model:', error);
        return false;
    } finally {
        isInitializing = false;
    }
}

export function stopFaceValidation() {
    if (faceDetectionInterval) {
        clearInterval(faceDetectionInterval);
        faceDetectionInterval = null;
    }
}

/**
 * Analyzes a frame for face presence, centering, and size
 */
export async function validateFaceInFrame(videoElementId, dotNetRef) {
    let video;
    if (typeof videoElementId === 'string') {
        video = document.getElementById(videoElementId);
    } else if (videoElementId && typeof videoElementId.addEventListener === 'function') {
        video = videoElementId;
    } else {
        video = document.getElementById('video-camera') ||
                document.getElementById('video-camera-login') ||
                document.getElementById('video-field-attendance') ||
                document.querySelector('video');
    }

    if (!video || video.readyState < 2) {
        return;
    }

    // Ensure model is ready
    if (!isModelLoaded) {
        await loadFaceDetectionModel();
        if (!isModelLoaded) return;
    }

    try {
        // Capture current frame
        sharedCanvas.width = video.videoWidth;
        sharedCanvas.height = video.videoHeight;
        sharedCtx.drawImage(video, 0, 0);

        // Run detection
        const result = faceDetector.detect(sharedCanvas);
        const detections = result.detections;

        let faceDetected = detections.length > 0;
        let faceCentered = false;
        let sizeGood = false;
        let lightingGood = true; // Still using basic brightness check for lighting
        let poseGood = true; // MediaPipe face detector focus
        
        let message = "Position your face in the frame";
        let isValid = false;

        if (faceDetected) {
            // Analyze the first/primary face detected
            const face = detections[0];
            const box = face.boundingBox;
            
            // Normalize coordinates (0-1)
            const nX = box.originX / sharedCanvas.width;
            const nY = box.originY / sharedCanvas.height;
            const nW = box.width / sharedCanvas.width;
            const nH = box.height / sharedCanvas.height;
            const nCenterX = nX + (nW / 2);
            const nCenterY = nY + (nH / 2);

            // 1. Centering check: Center of face should be within the middle 30% of the frame
            faceCentered = (nCenterX > 0.35 && nCenterX < 0.65) && (nCenterY > 0.3 && nCenterY < 0.7);

            // 2. Size check: Face should occupy between 20% and 60% of vertical space
            sizeGood = (nH > 0.25 && nH < 0.7);

            // 3. Lighting check
            const imageData = sharedCtx.getImageData(box.originX, box.originY, box.width, box.height);
            const brightness = calculateBrightness(imageData);
            lightingGood = brightness > 60 && brightness < 220;

            if (!faceCentered) {
                message = "Center your face in the frame";
            } else if (!sizeGood) {
                message = nH < 0.25 ? "Move closer to the camera" : "Move slightly further back";
            } else if (!lightingGood) {
                message = brightness < 60 ? "The area is too dark" : "The area is too bright";
            } else {
                message = "Perfect! Ready to capture.";
                isValid = true;
            }
        }

        if (dotNetRef) {
            const now = Date.now();
            if ((now - _lastFaceCallback) >= FACE_CALLBACK_THROTTLE_MS) {
                _lastFaceCallback = now;
                dotNetRef.invokeMethodAsync('OnFaceValidationResult', {
                    faceDetected,
                    faceCentered,
                    lightingGood,
                    poseGood,
                    sizeGood,
                    isValid,
                    message
                }).catch(() => {});
            }
        }

    } catch (err) {
        console.warn('[FaceDetection] Frame analysis error:', err);
    }
}

function calculateBrightness(imageData) {
    let sum = 0;
    const data = imageData.data;
    const pixelCount = data.length / 4;
    for (let i = 0; i < data.length; i += 4) {
        // Weighted grayscale
        sum += (data[i] * 0.299 + data[i + 1] * 0.587 + data[i + 2] * 0.114);
    }
    return sum / pixelCount;
}

/**
 * Real-time face validation loop
 */
export function startFaceValidation(videoElement, dotNetRef, intervalMs = 500) {
    stopFaceValidation();
    _lastFaceCallback = 0;
    faceDetectionInterval = setInterval(() => {
        validateFaceInFrame(videoElement, dotNetRef);
    }, intervalMs);
    return faceDetectionInterval;
}
