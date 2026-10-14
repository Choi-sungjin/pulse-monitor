"""Sortable process table. Unknown GPU readings stay last in either direction."""
import gi

gi.require_version('Gtk', '3.0')
from gi.repository import Gtk

COLUMNS = ('PID', '프로세스', 'CPU %', '메모리 MiB', 'GPU 연산 %', 'GPU 메모리 MB')


class ProcessTable(Gtk.TreeView):
    def __init__(self):
        self.store = Gtk.ListStore(int, str, float, float, float, float)
        super().__init__(model=self.store)
        self.get_accessible().set_name('열 제목으로 정렬하는 프로세스별 사용량')
        self.set_headers_clickable(True)
        self.set_search_column(1)
        for index, name in enumerate(COLUMNS):
            renderer = Gtk.CellRendererText()
            column = Gtk.TreeViewColumn(name, renderer, text=index)
            column.set_expand(index == 1)
            column.set_resizable(True)
            column.set_sort_column_id(index)
            self.store.set_sort_func(index, self.compare, index)
            if index >= 2:
                renderer.set_property('xalign', 1.0)
                column.set_cell_data_func(renderer, self.render_number, index)
            self.append_column(column)
        self.store.set_sort_column_id(2, Gtk.SortType.DESCENDING)
        self.set_tooltip_text('열 제목 클릭: 오름차순 / 내림차순. GPU --: 미지원 또는 측정값 없음. GPU 연산은 SM 사용률.')

    @staticmethod
    def render_number(column, cell, model, iterator, index):
        value = model[iterator][index]
        cell.set_property('text', '--' if value < 0 else f'{value:.1f}')

    @staticmethod
    def compare(model, first, second, index):
        left, right = model[first][index], model[second][index]
        if index >= 4 and (left < 0 or right < 0):
            result = (left < 0) - (right < 0)
            # GTK reverses comparisons in descending mode, including missing values.
            if model.get_sort_column_id()[1] == Gtk.SortType.DESCENDING:
                result = -result
            if result:
                return result
        if index == 1:
            left, right = left.casefold(), right.casefold()
        result = (left > right) - (left < right)
        if not result:
            a, b = model[first][0], model[second][0]
            result = (a > b) - (a < b)
        return result

    def refresh(self, processes):
        # Keep existing rows so live updates do not reset the scroll position.
        existing = {row[0]: row.iter for row in self.store}
        incoming = {p['pid'] for p in processes}
        for pid, iterator in existing.items():
            if pid not in incoming:
                self.store.remove(iterator)
        for p in processes:
            values = [p['pid'], p['name'], p['cpu'], p['rss'] / 2**20,
                      p['gpu'] if p.get('gpu') is not None else -1,
                      p['gpu_memory'] if p.get('gpu_memory') is not None else -1]
            iterator = existing.get(p['pid'])
            if iterator is None:
                self.store.append(values)
            elif list(self.store[iterator]) != values:
                self.store.set(iterator, dict(enumerate(values)))
