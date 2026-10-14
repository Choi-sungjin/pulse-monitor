#!/usr/bin/python3
"""Pulse Linux: native GTK window and Ubuntu panel indicator."""
import argparse
import json
import os
from pathlib import Path
import threading
import time

from metrics import Collector, percent

ROOT = Path(__file__).resolve().parent
APP_ID = 'io.github.choi_sungjin.PulseMonitor'


def fmt(value, suffix='', digits=1):
    return '--' if value is None else f'{value:.{digits}f}{suffix}'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--background', action='store_true')
    parser.add_argument('--diagnostics', action='store_true')
    options = parser.parse_args()
    if options.diagnostics:
        collector = Collector()
        collector.sample()
        time.sleep(0.3)
        print(json.dumps(collector.sample(), ensure_ascii=False, indent=2))
        return

    import gi
    gi.require_version('Gtk', '3.0')
    gi.require_version('AyatanaAppIndicator3', '0.1')
    from gi.repository import Gtk, GLib, Gio, Gdk, AyatanaAppIndicator3 as Indicator
    from process_table import ProcessTable

    class Pulse(Gtk.Application):
        def __init__(self):
            super().__init__(application_id=APP_ID, flags=Gio.ApplicationFlags.FLAGS_NONE)
            self.started = False
            self.stop = threading.Event()
            self.last_update = 0
            self.snapshot = None

        def do_activate(self):
            if self.started:
                self.window.show_all()
                self.window.present()
                return
            self.started = True
            self.hold()
            self.window = Gtk.ApplicationWindow(application=self, title='Pulse Monitor')
            self.window.set_default_size(960, 650)
            self.window.set_border_width(20)
            self.window.set_icon_from_file(str(ROOT / 'PulseWidget.png'))
            self.window.connect('delete-event', self.hide_window)
            self.window.connect('key-press-event', self.key_press)
            layout = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=14)
            self.window.add(layout)
            heading = Gtk.Label(xalign=0)
            heading.set_markup('<span size="xx-large" weight="bold">Pulse</span>  ·  시스템 모니터')
            layout.pack_start(heading, False, False, 0)
            self.summary = Gtk.Label(label='수집 중…', xalign=0)
            self.summary.set_selectable(True)
            self.summary.set_line_wrap(True)
            layout.pack_start(self.summary, False, False, 0)
            layout.pack_start(Gtk.Separator(), False, False, 0)
            label = Gtk.Label(label='전체 프로세스 · 열 제목 클릭으로 정렬 ↕ · CPU 100% = 코어 1개', xalign=0)
            layout.pack_start(label, False, False, 0)
            self.table = ProcessTable()
            self.store = self.table.store
            scroll = Gtk.ScrolledWindow()
            scroll.set_policy(Gtk.PolicyType.AUTOMATIC, Gtk.PolicyType.AUTOMATIC)
            scroll.add(self.table)
            layout.pack_start(scroll, True, True, 0)
            self.status = Gtk.Label(label='2초마다 갱신 · 창을 닫아도 상단 바에서 실행 중', xalign=0)
            layout.pack_start(self.status, False, False, 0)
            buttons = Gtk.Box(spacing=10)
            hide = Gtk.Button.new_with_mnemonic('창 숨기기(_H)')
            hide.connect('clicked', lambda *_: self.window.hide())
            leave = Gtk.Button.new_with_mnemonic('Pulse 종료(_Q)')
            leave.connect('clicked', self.exit_app)
            buttons.pack_end(leave, False, False, 0)
            buttons.pack_end(hide, False, False, 0)
            layout.pack_start(buttons, False, False, 0)
            self.indicator = Indicator.Indicator.new('pulse-monitor', str(ROOT / 'PulseWidget.png'),
                                                     Indicator.IndicatorCategory.SYSTEM_SERVICES)
            self.indicator.set_status(Indicator.IndicatorStatus.ACTIVE)
            self.indicator.set_title('Pulse Monitor')
            self.indicator.set_label('C --  G --  R --', 'C 100  G 100  R 100')
            menu = Gtk.Menu()
            details = Gtk.MenuItem(label='상세 정보 열기')
            details.connect('activate', lambda *_: self.do_activate())
            menu.append(details)
            self.menu_status = Gtk.MenuItem(label='수집 중…')
            self.menu_status.set_sensitive(False)
            menu.append(self.menu_status)
            menu.append(Gtk.SeparatorMenuItem())
            leave_item = Gtk.MenuItem(label='Pulse 종료')
            leave_item.connect('activate', self.exit_app)
            menu.append(leave_item)
            menu.show_all()
            self.indicator.set_menu(menu)
            self.indicator.set_secondary_activate_target(details)
            if not options.background:
                self.window.show_all()
            threading.Thread(target=self.collect, daemon=True).start()
            GLib.timeout_add_seconds(1, self.check_stale)

        def hide_window(self, *_):
            self.window.hide()
            return True

        def key_press(self, widget, event):
            if event.keyval == Gdk.KEY_Escape:
                return self.hide_window()
            return False

        def collect(self):
            collector = Collector()
            # Prime CPU/process delta counters before showing the first result.
            try:
                collector.sample()
            except Exception:
                pass
            while not self.stop.wait(2):
                try:
                    snapshot = collector.sample()
                    GLib.idle_add(self.update, snapshot)
                except Exception as error:
                    GLib.idle_add(self.failed, type(error).__name__)

        def failed(self, reason):
            self.last_update = 0
            self.status.set_text('수집 실패 · ' + reason)
            self.clear_values()
            return False

        def clear_values(self):
            self.indicator.set_label('C --  G --  R --', 'C 100  G 100  R 100')
            self.menu_status.set_label('수집 실패 또는 갱신 지연')
            self.summary.set_text('수치를 읽을 수 없어 -- 로 표시 중이야.')
            self.store.clear()

        def check_stale(self):
            if self.last_update and time.monotonic() - self.last_update > 8:
                self.clear_values()
                self.status.set_text('갱신 지연 · 다음 수집 대기 중')
            return not self.stop.is_set()

        def update(self, snapshot):
            if self.stop.is_set():
                return False
            self.last_update = time.monotonic()
            self.snapshot = snapshot
            s = snapshot
            gpu = s['gpus'][0] if s['gpus'] else {}
            self.indicator.set_label(f"C {percent(s['cpu'])}  G {percent(gpu.get('usage'))}  R {percent(s['ram_percent'])}",
                                     'C 100  G 100  R 100')
            lines = [f"CPU  {s['cpu']:.1f}%  ·  온도 {fmt(s['cpu_temp'], ' °C')}",
                     f"메모리  {s['ram_percent']:.1f}%  ·  {s['ram_used']/2**30:.1f} / {s['ram_total']/2**30:.1f} GiB"]
            for g in s['gpus']:
                lines.append(f"{g['name']}\nGPU  {fmt(g['usage'], '%')}  ·  {fmt(g['temp'], ' °C')}  ·  {fmt(g['power'], ' W')}"
                             f"  ·  VRAM {fmt(g['used'], digits=0)} / {fmt(g['total'], ' MiB', 0)}")
            if not s['gpus']:
                lines.append('GPU  -- · NVIDIA 센서 없음 또는 읽기 실패')
            down = s['down']/1024 if s['down'] is not None else None
            up = s['up']/1024 if s['up'] is not None else None
            lines.append(f"네트워크 전체  ↓ {fmt(down, ' KiB/s')}  ↑ {fmt(up, ' KiB/s')}")
            self.summary.set_text('\n'.join(lines))
            self.menu_status.set_label(f"CPU {s['cpu']:.1f}% · RAM {s['ram_percent']:.1f}%")
            self.table.refresh(s['processes'])
            self.status.set_text(time.strftime('%H:%M:%S') + ' 갱신 · 2초 주기 · 닫으면 상단 바에서 실행')
            return False

        def exit_app(self, *_):
            self.stop.set()
            self.indicator.set_status(Indicator.IndicatorStatus.PASSIVE)
            self.quit()

        def do_shutdown(self):
            self.stop.set()
            Gtk.Application.do_shutdown(self)

    app = Pulse()
    app.run(['pulse-monitor'])


if __name__ == '__main__':
    main()
