"""Read CPU, RAM, GPU stats. Works on Windows with NVIDIA or AMD."""
import psutil

# Try NVIDIA first, fall back to a stub
try:
    import pynvml
    pynvml.nvmlInit()
    _GPU_HANDLE = pynvml.nvmlDeviceGetHandleByIndex(0)
    _GPU_NAME = pynvml.nvmlDeviceGetName(_GPU_HANDLE)
    if isinstance(_GPU_NAME, bytes):
        _GPU_NAME = _GPU_NAME.decode()
    _NVIDIA = True
except Exception:
    _NVIDIA = False
    _GPU_HANDLE = None
    _GPU_NAME = "GPU"


def get_gpu_vram_total_gb() -> float:
    """Total VRAM in GB (NVIDIA only). Returns 0 if unavailable."""
    if not _NVIDIA:
        return 0.0
    try:
        info = pynvml.nvmlDeviceGetMemoryInfo(_GPU_HANDLE)
        return round(info.total / 1024 ** 3, 1)
    except Exception:
        return 0.0


def get_gpu_vram_free_gb() -> float:
    if not _NVIDIA:
        return 0.0
    try:
        info = pynvml.nvmlDeviceGetMemoryInfo(_GPU_HANDLE)
        return round(info.free / 1024 ** 3, 1)
    except Exception:
        return 0.0


def get_snapshot() -> dict:
    """Return a dict with current CPU %, RAM %, GPU util %, GPU mem %."""
    cpu = psutil.cpu_percent(interval=None)
    ram = psutil.virtual_memory().percent

    gpu_util = 0.0
    gpu_mem = 0.0
    if _NVIDIA:
        try:
            util = pynvml.nvmlDeviceGetUtilizationRates(_GPU_HANDLE)
            gpu_util = float(util.gpu)
            mem_info = pynvml.nvmlDeviceGetMemoryInfo(_GPU_HANDLE)
            gpu_mem = round(mem_info.used / mem_info.total * 100, 1)
        except Exception:
            pass

    return {
        "cpu": cpu,
        "ram": ram,
        "gpu_util": gpu_util,
        "gpu_mem": gpu_mem,
        "gpu_name": _GPU_NAME,
    }


def get_system_info() -> dict:
    """Static info used for model recommendations."""
    ram_gb = round(psutil.virtual_memory().total / 1024 ** 3, 1)
    cpu_cores = psutil.cpu_count(logical=False) or 4
    vram_gb = get_gpu_vram_total_gb()
    return {
        "ram_gb": ram_gb,
        "cpu_cores": cpu_cores,
        "vram_gb": vram_gb,
        "gpu_name": _GPU_NAME,
    }
