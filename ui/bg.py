"""Thread-safe bridge from worker threads to the Tk main loop.

Tk is not thread-safe: calling widget methods (even .after) from a worker
thread can raise "main thread is not in main loop" or silently do nothing.
Workers call `ui(fn, *args)`; the main loop drains the queue every 30 ms.
"""
import queue
import threading
import traceback

_q: "queue.Queue[tuple]" = queue.Queue()


def ui(fn, *args):
    """Schedule fn(*args) on the Tk main thread. Safe from any thread."""
    _q.put((fn, args))


def start(root):
    """Call once from the main thread after the root window exists."""
    def tick():
        for _ in range(1000):
            try:
                fn, args = _q.get_nowait()
            except queue.Empty:
                break
            try:
                fn(*args)
            except Exception:
                traceback.print_exc()
        root.after(30, tick)
    root.after(30, tick)


def run(fn, *args, on_done=None, on_error=None):
    """Run fn(*args) in a daemon thread; deliver the result on the UI thread."""
    def worker():
        try:
            result = fn(*args)
        except Exception as e:  # noqa: BLE001
            if on_error:
                ui(on_error, e)
            return
        if on_done:
            ui(on_done, result)
    threading.Thread(target=worker, daemon=True).start()
