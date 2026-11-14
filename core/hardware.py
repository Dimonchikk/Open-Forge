"""CPU / RAM / GPU stats.

NVIDIA: live load and VRAM use through NVML.
AMD / Intel on Windows: name and total VRAM from the display-adapter registry
keys (live GPU load isn't exposed there, so it shows as unavailable).
"""
import sys
import psutil

_nv = None
try:
    import pynvml
    pynvml.nvmlInit()
    _nv = pynvml.nvmlDeviceGetHandleByIndex(0)
except Exception:
    _nv = None


def _registry_gpu() -> tuple[str, float]:
    """(name, vram_gb) of the adapter with the most memory, from the registry."""
    if sys.platform != "win32":
        return "", 0.0
    try:
        import winreg
    except ImportError:
        return "", 0.0
    base = r"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"
    best = ("", 0.0)
    try:
        root = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, base)
    except OSError:
        return best
    for i in range(32):
        try:
            sub = winreg.EnumKey(root, i)
        except OSError:
            break
        try:
            k = winreg.OpenKey(root, sub)
        except OSError:
            continue
        try:
            name = winreg.QueryValueEx(k, "DriverDesc")[0]
        except OSError:
            name = ""
        size = 0
        for value in ("HardwareInformation.qwMemorySize", "HardwareInformation.MemorySize"):
            try:
                raw = winreg.QueryValueEx(k, value)[0]
                size = int.from_bytes(raw, "little") if isinstance(raw, bytes) else int(raw)
                if size:
                    break
            except (OSError, ValueError, TypeError):
                pass
        gb = size / 1024 ** 3
        if gb > best[1]:
            best = (name, gb)
    return best


def _gpu_static() -> tuple[str, float]:
    if _nv is not None:
        try:
            name = pynvml.nvmlDeviceGetName(_nv)
            name = name.decode() if isinstance(name, bytes) else name
            total = pynvml.nvmlDeviceGetMemoryInfo(_nv).total / 1024 ** 3
            return name, total
        except Exception:
            pass
    return _registry_gpu()


_GPU_NAME, _VRAM_GB = _gpu_static()
_GPU_NAME = (_GPU_NAME or "").replace("NVIDIA GeForce ", "").replace("AMD Radeon ", "Radeon ")
psutil.cpu_percent(interval=None)  # prime: the first call always returns 0


def get_snapshot() -> dict:
    """Live values. gpu_util / vram_used_gb are None when not measurable."""
    vm = psutil.virtual_memory()
    snap = {
        "cpu": psutil.cpu_percent(interval=None),
        "ram": vm.percent,
        "ram_used_gb": vm.used / 1024 ** 3,
        "ram_total_gb": vm.total / 1024 ** 3,
        "gpu_util": None,
        "vram": None,
        "vram_used_gb": None,
        "vram_total_gb": _VRAM_GB,
        "gpu_name": _GPU_NAME,
    }
    if _nv is not None:
        try:
            snap["gpu_util"] = float(pynvml.nvmlDeviceGetUtilizationRates(_nv).gpu)
            mem = pynvml.nvmlDeviceGetMemoryInfo(_nv)
            snap["vram_used_gb"] = mem.used / 1024 ** 3
            snap["vram"] = mem.used / mem.total * 100
        except Exception:
            pass
    return snap


def get_system_info() -> dict:
    """Static info used for model recommendations."""
    return {
        "ram_gb": round(psutil.virtual_memory().total / 1024 ** 3),
        "cpu_cores": psutil.cpu_count(logical=False) or 4,
        "cpu_threads": psutil.cpu_count(logical=True) or 4,
        "vram_gb": round(_VRAM_GB),
        "gpu_name": _GPU_NAME,
    }
