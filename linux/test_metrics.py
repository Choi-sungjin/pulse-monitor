import subprocess
import unittest
from unittest.mock import patch

from metrics import Collector, gpu_readings, number, parse_gpu, percent, parse_process_gpu, process_gpu_readings


class MetricsTests(unittest.TestCase):
    def test_percent_boundaries(self):
        for value, expected in [(None, '--'), ('N/A', '--'), (float('nan'), '--'),
                                (float('inf'), '--'), (-1, '--'), (0, '0'),
                                (42.2, '42'), (100, '100'), (101, '100')]:
            with self.subTest(value=value):
                self.assertEqual(percent(value), expected)

    def test_gpu_optional_metrics_and_multiple_devices(self):
        rows = parse_gpu('GPU A, 20, 1024, 8192, 43, 25.6\nGPU B, N/A, 0, 4096, [Not Supported], N/A\n')
        self.assertEqual(len(rows), 2)
        self.assertEqual(rows[0]['usage'], 20)
        self.assertIsNone(rows[1]['usage'])
        self.assertIsNone(rows[1]['temp'])
        self.assertEqual(rows[1]['used'], 0)

    def test_broken_gpu_rows(self):
        self.assertEqual(parse_gpu('garbage\n\n1,2,3'), [])

    @patch('metrics.shutil.which', return_value=None)
    def test_no_nvidia(self, _):
        self.assertEqual(gpu_readings(), [])

    @patch('metrics.shutil.which', return_value='/usr/bin/nvidia-smi')
    @patch('metrics.subprocess.run', side_effect=subprocess.TimeoutExpired('nvidia-smi', 2))
    def test_gpu_timeout(self, *_):
        self.assertEqual(gpu_readings(), [])

    @patch('metrics.gpu_readings', return_value=[])
    @patch('metrics.time.monotonic', side_effect=[1, 3, 5])
    def test_network_delta_and_counter_reset(self, *_):
        from types import SimpleNamespace
        with patch('metrics.psutil.net_io_counters', side_effect=[
            SimpleNamespace(bytes_recv=100, bytes_sent=200),
            SimpleNamespace(bytes_recv=300, bytes_sent=600),
            SimpleNamespace(bytes_recv=10, bytes_sent=10)]):
            c = Collector()
            self.assertIsNone(c.sample()['down'])
            second = c.sample()
            self.assertEqual(second['down'], 100)
            self.assertEqual(second['up'], 200)
            self.assertEqual(c.sample()['down'], 0)

    def test_process_gpu_headers_and_unknowns(self):
        readings = parse_process_gpu("""# gpu pid type sm mem enc dec jpg ofa fb ccpm command
# Idx # C/G % % % % % % MB MB name
0 42 G 10 5 - - - - 100 0 app with spaces
0 55 G - - - - - - 0 0 app
bad input
0 - - - - - - - - - - -
""")
        self.assertEqual(readings[42], {'gpu': 10, 'gpu_memory': 100})
        self.assertEqual(readings[55], {'gpu': None, 'gpu_memory': 0})
        self.assertEqual(parse_process_gpu('driver error'), {})
        older = parse_process_gpu('# gpu pid type sm mem enc dec fb command\n0 1 G 0 0 - - 50 app')
        self.assertEqual(older[1], {'gpu': 0, 'gpu_memory': 50})

    def test_process_gpu_multiple_devices_and_duplicate_rows(self):
        rows = '# gpu pid sm fb\n0 42 10 100\n0 42 10 100\n1 42 20 200'
        self.assertEqual(parse_process_gpu(rows)[42], {'gpu': 30, 'gpu_memory': 300})
        self.assertIsNone(parse_process_gpu(rows+'\n2 42 - 50')[42]['gpu'])

    @patch('metrics.shutil.which', return_value='/usr/bin/nvidia-smi')
    @patch('metrics.subprocess.run', side_effect=subprocess.TimeoutExpired('nvidia-smi', 2))
    def test_process_gpu_timeout(self, *_):
        self.assertEqual(process_gpu_readings(), {})

    def test_live_collection(self):
        import time
        c = Collector()
        c.sample()
        time.sleep(0.15)
        s = c.sample()
        self.assertTrue(0 <= s['cpu'] <= 100)
        self.assertTrue(0 <= s['ram_percent'] <= 100)
        self.assertGreater(s['ram_total'], 0)
        self.assertTrue(s['processes'])
        self.assertTrue(all(p['rss'] >= 0 for p in s['processes']))
        self.assertNotIn('cmdline', s['processes'][0])


if __name__ == '__main__':
    unittest.main()
