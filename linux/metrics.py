"""Read Linux metrics without elevated privileges or process command lines."""
import csv
import math
import shutil
import subprocess
import time

import psutil


def number(value):
    try:
        result = float(value)
        return result if math.isfinite(result) and result >= 0 else None
    except (ValueError, TypeError):
        return None


def percent(value):
    value = number(value)
    return '--' if value is None else str(round(min(100, value)))


def parse_gpu(text):
    rows = []
    for row in csv.reader(text.splitlines()):
        if len(row) != 6:
            continue
        name, usage, used, total, temp, power = [x.strip() for x in row]
        rows.append(dict(name=name, usage=number(usage), used=number(used),
                         total=number(total), temp=number(temp), power=number(power)))
    return rows


def gpu_readings():
    command = shutil.which('nvidia-smi')
    if not command:
        return []
    try:
        result = subprocess.run([
            command, '--query-gpu=name,utilization.gpu,memory.used,memory.total,temperature.gpu,power.draw',
            '--format=csv,noheader,nounits'], capture_output=True, text=True, timeout=2, check=True)
        return parse_gpu(result.stdout)
    except (OSError, subprocess.SubprocessError):
        return []


def parse_process_gpu(text):
    """Read pmon's named columns; do not depend on driver-specific column counts."""
    columns = None
    devices = {}
    for line in text.splitlines():
        fields = line.split()
        if line.startswith('#'):
            header = line.lstrip('#').split()
            if all(name in header for name in ('gpu', 'pid', 'sm', 'fb')):
                columns = {name: header.index(name) for name in ('gpu', 'pid', 'sm', 'fb')}
            continue
        if not columns or len(fields) <= max(columns.values()):
            continue
        try:
            gpu, pid = (int(fields[columns[key]]) for key in ('gpu', 'pid'))
        except ValueError:
            continue
        if gpu < 0 or pid <= 0:
            continue
        devices[(gpu, pid)] = (number(fields[columns['sm']]), number(fields[columns['fb']]))
    by_pid = {}
    for (_, pid), values in devices.items():
        by_pid.setdefault(pid, []).append(values)
    # If one reported device cannot supply a metric, its total is unknown.
    return {pid: dict(gpu=(sum(v[0] for v in rows) if all(v[0] is not None for v in rows) else None),
                      gpu_memory=(sum(v[1] for v in rows) if all(v[1] is not None for v in rows) else None))
            for pid, rows in by_pid.items()}


def process_gpu_readings():
    command = shutil.which('nvidia-smi')
    if not command:
        return {}
    try:
        result = subprocess.run([command, 'pmon', '-c', '1', '-s', 'um'],
                                capture_output=True, text=True, timeout=2, check=True)
        return parse_process_gpu(result.stdout)
    except (OSError, subprocess.SubprocessError):
        return {}


class Collector:
    def __init__(self):
        self.previous_net = None
        self.previous_time = None
        psutil.cpu_percent(None)

    def sample(self):
        now = time.monotonic()
        cpu = psutil.cpu_percent(None)
        ram = psutil.virtual_memory()
        net = psutil.net_io_counters()
        down = up = None
        if self.previous_net is not None and now > self.previous_time:
            elapsed = now - self.previous_time
            down = max(0, net.bytes_recv - self.previous_net.bytes_recv) / elapsed
            up = max(0, net.bytes_sent - self.previous_net.bytes_sent) / elapsed
        self.previous_net, self.previous_time = net, now
        processes = []
        for process in psutil.process_iter(['pid', 'name', 'memory_info']):
            try:
                info = process.info
                if info['memory_info'] is None:
                    continue
                processes.append(dict(pid=info['pid'], name=info['name'] or '?',
                    cpu=process.cpu_percent(None), rss=info['memory_info'].rss))
            except (psutil.Error, OSError):
                continue
        gpu_processes = process_gpu_readings()
        for process in processes:
            reading = gpu_processes.get(process['pid'], {})
            process.update(gpu=reading.get('gpu'), gpu_memory=reading.get('gpu_memory'))
        temperature = None
        try:
            sensors = psutil.sensors_temperatures()
            for key in ('coretemp', 'k10temp', 'zenpower', 'cpu_thermal'):
                readings = sensors.get(key, [])
                values = [number(x.current) for x in readings]
                values = [x for x in values if x is not None]
                if values:
                    temperature = max(values)
                    break
        except (OSError, AttributeError):
            pass
        return dict(timestamp=time.time(), cpu=cpu, cpu_temp=temperature,
                    ram_percent=ram.percent, ram_used=ram.total-ram.available,
                    ram_total=ram.total, down=down, up=up,
                    gpus=gpu_readings(), processes=processes)
