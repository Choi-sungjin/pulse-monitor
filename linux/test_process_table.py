import unittest

from process_table import Gtk, ProcessTable


def process(pid, name, cpu, memory, gpu=None, gpu_memory=None):
    return dict(pid=pid, name=name, cpu=cpu, rss=memory*2**20, gpu=gpu, gpu_memory=gpu_memory)


class SortingTests(unittest.TestCase):
    def setUp(self):
        self.table = ProcessTable()
        self.rows = [process(100, 'alpha', 10, 100, None, None),
                     process(2, 'Zulu', 100, 2, 0, 10),
                     process(10, 'Beta', 2, 10, 25, 0)]
        self.table.refresh(self.rows)

    def ids(self):
        return [row[0] for row in self.table.store]

    def tearDown(self):
        self.table.destroy()

    def test_header_clicks_both_directions_all_columns(self):
        expected = [[2, 10, 100], [100, 10, 2], [10, 100, 2],
                    [2, 10, 100], [2, 10, 100], [10, 2, 100]]
        for index, ascending in enumerate(expected):
            with self.subTest(column=index):
                self.table.store.set_sort_column_id(index, Gtk.SortType.DESCENDING)
                self.table.get_column(index).clicked()
                self.assertEqual(self.ids(), ascending)
                self.table.get_column(index).clicked()
                descending = list(reversed(ascending)) if index < 4 else list(reversed(ascending[:-1]))+[100]
                self.assertEqual(self.ids(), descending)

    def test_refresh_keeps_sort_and_all_rows(self):
        self.table.store.set_sort_column_id(3, Gtk.SortType.DESCENDING)
        rows = [process(i, str(i), 0, i) for i in range(1, 101)]
        self.table.refresh(rows)
        self.assertEqual(self.ids(), list(range(100, 0, -1)))
        self.assertEqual(self.table.store.get_sort_column_id(), (3, Gtk.SortType.DESCENDING))
        rows[0]['rss'] = 200*2**20
        self.table.refresh(rows)
        self.assertEqual(self.ids()[0], 1)

    def test_refresh_keeps_scroll_position(self):
        import time
        window = Gtk.Window()
        window.set_default_size(960, 350)
        scroll = Gtk.ScrolledWindow()
        scroll.add(self.table)
        window.add(scroll)
        rows = [process(i, str(i), i, i) for i in range(1, 101)]
        self.table.refresh(rows)
        window.show_all()

        def settle():
            for _ in range(15):
                while Gtk.events_pending():
                    Gtk.main_iteration()
                time.sleep(.01)

        try:
            settle()
            adjustment = scroll.get_vadjustment()
            adjustment.set_value(600)
            settle()
            self.table.refresh(rows)
            settle()
            self.assertAlmostEqual(adjustment.get_value(), 600, delta=1)
        finally:
            scroll.remove(self.table)
            window.destroy()

    def test_empty_and_unknown_and_selection(self):
        self.table.get_selection().select_path(0)
        selected = self.ids()[0]
        self.table.refresh(list(reversed(self.rows)))
        model, iterator = self.table.get_selection().get_selected()
        self.assertEqual(model[iterator][0], selected)
        cell = Gtk.CellRendererText()
        unknown = next(row.iter for row in self.table.store if row[0] == 100)
        self.table.render_number(None, cell, self.table.store, unknown, 4)
        self.assertEqual(cell.get_property('text'), '--')
        self.table.refresh([])
        self.assertEqual(self.ids(), [])
        self.table.refresh([process(1, 'a', 0, 0), process(2, 'b', 0, 0)])
        self.table.store.set_sort_column_id(4, Gtk.SortType.ASCENDING)
        self.assertEqual(len(self.ids()), 2)


if __name__ == '__main__':
    unittest.main()
