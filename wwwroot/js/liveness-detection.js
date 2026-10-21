// Liveness — simple movement prompts: left, right, closer, up/down.

const DEFAULTS = {
    requiredScore: 10,
    requiredSustained: 3,
    motionThreshold: 3,
    sampleIntervalMs: 100,
    minElapsedMs: 600,
    callbackThrottleMs: 350,
    timeoutMs: 12000,
    generousThreshold: 0.5,
    analysisMaxWidth: 320,
};

const CHALLENGES = ['turnLeft', 'turnRight', 'comeCloser', 'moveUpDown'];

const INSTRUCTIONS = {
    turnLeft: 'Move your head to the left',
    turnRight: 'Move your head to the right',
    comeCloser: 'Move closer to the camera',
    moveUpDown: 'Move your head up and down',
};

let _state = 'idle';
let challengeType = null;
let challengeStartTime = null;
let challengeCompleted = false;
let monitorInterval = null;
let lastCallbackTime = 0;
let _cfg = { ...DEFAULTS };

export function getLivenessState() {
    return _state;
}

export function stopLivenessChallenge() {
    if (monitorInterval) {
        clearInterval(monitorInterval);
        monitorInterval = null;
    }
    challengeCompleted = false;
}

export function resetLivenessChallenge() {
    stopLivenessChallenge();
    _state = 'idle';
    challengeType = null;
    challengeStartTime = null;
    challengeCompleted = false;
    lastCallbackTime = 0;
    _cfg = { ...DEFAULTS };
}

function _mergeConfig(options) {
    if (!options || typeof options !== 'object') return { ...DEFAULTS };
    const merged = { ...DEFAULTS };
    for (const key of Object.keys(DEFAULTS)) {
        if (options[key] !== undefined && options[key] !== null) {
            const n = Number(options[key]);
            if (!Number.isNaN(n)) merged[key] = n;
        }
    }
    return merged;
}

export function startLivenessChallenge(videoElementId, dotNetRef, options) {
    stopLivenessChallenge();
    _cfg = _mergeConfig(options);

    let videoElement;
    if (typeof videoElementId === 'string') {
        videoElement = document.getElementById(videoElementId);
    } else if (videoElementId && typeof videoElementId.addEventListener === 'function') {
        videoElement = videoElementId;
    } else {
        videoElement = document.getElementById('video-camera') ||
                       document.getElementById('video-camera-login') ||
                       document.querySelector('video');
    }

    if (!videoElement) {
        _state = 'failed';
        _throttledCallback(dotNetRef, 'error', 'Camera not found.', true);
        return;
    }

    challengeType = CHALLENGES[Math.floor(Math.random() * CHALLENGES.length)];
    challengeStartTime = Date.now();
    challengeCompleted = false;
    lastCallbackTime = 0;
    _state = 'challenging';

    const msg = INSTRUCTIONS[challengeType] || 'Follow the arrow';
    _throttledCallback(dotNetRef, challengeType, msg, true);
    _runMonitor(videoElement, dotNetRef);
    return challengeType;
}

export function getChallengeType() {
    return challengeType;
}

function _throttledCallback(dotNetRef, status, message, force = false) {
    if (!dotNetRef) return;
    const now = Date.now();
    if (!force && (now - lastCallbackTime) < _cfg.callbackThrottleMs) return;
    lastCallbackTime = now;
    dotNetRef.invokeMethodAsync('OnLivenessChallengeUpdate', status, message).catch(() => {});
}

function _scoreFrameForChallenge(diffs, type) {
    const t = _cfg.motionThreshold;
    const { left, right, center, centerTop, centerBottom } = diffs;

    switch (type) {
        case 'turnLeft':
            return left > right + 1 && left > t ? Math.min(left / t, 4) : 0;
        case 'turnRight':
            return right > left + 1 && right > t ? Math.min(right / t, 4) : 0;
        case 'comeCloser':
            return center > Math.max(left, right) + 1 && center > t ? Math.min(center / t, 4) : 0;
        case 'moveUpDown': {
            const vertical = Math.max(centerTop, centerBottom);
            return vertical > t ? Math.min(vertical / t, 4) : 0;
        }
        default:
            return Math.max(left, right, center) > t ? 2 : 0;
    }
}

function _runMonitor(videoElement, dotNetRef) {
    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d', { willReadFrequently: true });

    if (!videoElement || videoElement.readyState < 2) {
        _state = 'failed';
        _throttledCallback(dotNetRef, 'error', 'Camera not ready. Wait a moment and try again.', true);
        return;
    }

    const vw = videoElement.videoWidth || 640;
    const vh = videoElement.videoHeight || 480;
    const scale = Math.min(1, _cfg.analysisMaxWidth / vw);
    canvas.width = Math.max(160, Math.floor(vw * scale));
    canvas.height = Math.max(120, Math.floor(vh * scale));

    let prevSamples = null;
    let motionScore = 0;
    let sustainedFrames = 0;
    let warmupFrames = 0;
    const instruction = INSTRUCTIONS[challengeType] || 'Follow the arrow';

    function getRegionDefs(w, h) {
        return {
            left: { x: w * 0.05, y: h * 0.28, w: w * 0.28, h: h * 0.44 },
            right: { x: w * 0.67, y: h * 0.28, w: w * 0.28, h: h * 0.44 },
            center: { x: w * 0.34, y: h * 0.22, w: w * 0.32, h: h * 0.5 },
            centerTop: { x: w * 0.34, y: h * 0.12, w: w * 0.32, h: h * 0.22 },
            centerBottom: { x: w * 0.34, y: h * 0.58, w: w * 0.32, h: h * 0.22 },
        };
    }

    function brightnessForRegion(r) {
        const id = ctx.getImageData(Math.floor(r.x), Math.floor(r.y), Math.floor(r.w), Math.floor(r.h));
        let sum = 0;
        let samples = 0;
        for (let i = 0; i < id.data.length; i += 16) {
            sum += id.data[i] * 0.299 + id.data[i + 1] * 0.587 + id.data[i + 2] * 0.114;
            samples++;
        }
        return samples > 0 ? sum / samples : 0;
    }

    function drawVideoToCanvas() {
        // Mirror horizontally so analysis matches the selfie preview (scaleX(-1) on login UI).
        // Without this, "turn left" on screen produces motion on the wrong side of the frame.
        ctx.save();
        ctx.setTransform(1, 0, 0, 1, 0, 0);
        ctx.translate(canvas.width, 0);
        ctx.scale(-1, 1);
        ctx.drawImage(videoElement, 0, 0, canvas.width, canvas.height);
        ctx.restore();
    }

    function sampleFrame() {
        try {
            drawVideoToCanvas();
        } catch {
            return null;
        }
        const defs = getRegionDefs(canvas.width, canvas.height);
        const out = {};
        for (const [key, rect] of Object.entries(defs)) {
            out[key] = brightnessForRegion(rect);
        }
        return out;
    }

    monitorInterval = setInterval(() => {
        if (challengeCompleted) {
            clearInterval(monitorInterval);
            monitorInterval = null;
            return;
        }

        const elapsed = Date.now() - challengeStartTime;

        if (videoElement.readyState < 2 || videoElement.videoWidth === 0) {
            return;
        }

        const current = sampleFrame();
        if (!current) return;

        warmupFrames++;
        if (warmupFrames <= 3) {
            prevSamples = current;
            return;
        }

        if (prevSamples) {
            const diffs = {};
            for (const key of Object.keys(current)) {
                diffs[key] = Math.abs(current[key] - prevSamples[key]);
            }

            const frameScore = _scoreFrameForChallenge(diffs, challengeType);
            if (frameScore > 0) {
                motionScore += frameScore;
                sustainedFrames++;
            } else {
                sustainedFrames = Math.max(0, sustainedFrames - 1);
            }
        }

        prevSamples = current;

        if (!challengeCompleted) {
            _state = 'verifying';
            _throttledCallback(dotNetRef, 'verifying', instruction);
        }

        const passed = motionScore >= _cfg.requiredScore && sustainedFrames >= _cfg.requiredSustained;

        if (passed && elapsed >= _cfg.minElapsedMs && !challengeCompleted) {
            challengeCompleted = true;
            _state = 'passed';
            clearInterval(monitorInterval);
            monitorInterval = null;
            _throttledCallback(dotNetRef, 'completed', 'Done! You can capture now.', true);
            return;
        }

        if (elapsed > _cfg.timeoutMs && !challengeCompleted) {
            clearInterval(monitorInterval);
            monitorInterval = null;
            if (motionScore > _cfg.requiredScore * _cfg.generousThreshold) {
                challengeCompleted = true;
                _state = 'passed';
                _throttledCallback(dotNetRef, 'completed', 'Done! You can capture now.', true);
            } else {
                _state = 'timedOut';
                _throttledCallback(dotNetRef, 'timeout',
                    `Timed out. Please try again: ${instruction}`, true);
            }
        }
    }, _cfg.sampleIntervalMs);
}
