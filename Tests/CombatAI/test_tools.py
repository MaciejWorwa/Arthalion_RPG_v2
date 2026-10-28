import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('summarize', ROOT / 'Tools/AI/summarize.py')
summary = importlib.util.module_from_spec(spec)
spec.loader.exec_module(summary)


class MetricsTests(unittest.TestCase):
    def test_outcomes_use_the_learned_side_and_exclude_aborts(self):
        rows = [dict(result=result, learned_side=side, decisions='2', seconds='1') for result, side in
                [('EnemyWin', 'Enemy'), ('PlayerWin', 'Enemy'), ('Draw', 'Player'), ('TechnicalAbort', 'Player')]]
        result = summary.summarize(rows)
        self.assertEqual((result['wins'], result['losses'], result['draws'], result['aborts']), (1, 1, 1, 1))
        self.assertAlmostEqual(result['win_rate'], 1/3)

    def test_empty_and_balanced_confidence(self):
        self.assertEqual(summary.wilson(0, 0), (0, 1))
        low, high = summary.wilson(200, 400)
        self.assertAlmostEqual(low, 1-high)
        self.assertLess(high-low, 0.1)


if __name__ == '__main__':
    unittest.main()
