import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('checkpoint', ROOT / 'Tools/AI/checkpoint.py')
checkpoint = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checkpoint)


class CheckpointTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        run = self.root / 'results/source'
        behavior = run / checkpoint.BEHAVIOR
        behavior.mkdir(parents=True)
        (run / 'run_logs').mkdir()
        (behavior / 'checkpoint.pt').write_bytes(b'optimizer-and-policy')
        (behavior / 'ArthalionCombatV1-42.pt').write_bytes(b'optimizer-and-policy')
        (behavior / 'ArthalionCombatV1-42.onnx').write_bytes(b'policy')
        (behavior / 'events.out.tfevents.123.host.0').write_bytes(b'curve')
        (run / 'ArthalionCombatV1.onnx').write_bytes(b'policy')
        metadata = {'model_sha256': checkpoint.digest(b'policy'), 'rules': 'same-rules'}
        (run / 'ArthalionCombatV1.metadata.json').write_text(json.dumps(metadata))
        status = {checkpoint.BEHAVIOR: {'final_checkpoint': dict(steps=42, reward=0.5, creation_time=123)},
                  'metadata': {'mlagents_version': '1.1.0'}}
        (run / 'run_logs/training_status.json').write_text(json.dumps(status))
        metrics = self.root / 'metrics/source'
        (metrics / 'worker_5005').mkdir(parents=True)
        (metrics / 'trainer.yaml').write_text('behaviors: {}')
        (metrics / 'worker_5005/episodes.csv').write_text('episode;result\n1;EnemyWin\n')
        self.bundle = self.root / 'portable.zip'
        checkpoint.export_run('source', self.bundle, self.root)

    def test_round_trip_preserves_optimizer_metrics_and_relocates_paths(self):
        other_pc = self.root / 'other-computer'
        checkpoint.import_run(self.bundle, 'continued', other_pc)
        run = other_pc / 'results/continued'
        self.assertEqual((run / checkpoint.BEHAVIOR / 'checkpoint.pt').read_bytes(), b'optimizer-and-policy')
        self.assertEqual((run / checkpoint.BEHAVIOR / 'events.out.tfevents.123.host.0').read_bytes(), b'curve')
        self.assertTrue((other_pc / 'metrics/continued/worker_5005/episodes.csv').is_file())
        status = json.loads((run / 'run_logs/training_status.json').read_text())
        final = status[checkpoint.BEHAVIOR]['final_checkpoint']
        self.assertEqual(final['steps'], 42)
        self.assertEqual(Path(final['file_path']), run / 'ArthalionCombatV1.onnx')
        self.assertTrue(Path(final['auxillary_file_paths'][0]).is_file())

    def test_never_overwrites_existing_run_or_archive(self):
        with self.assertRaises(ValueError):
            checkpoint.import_run(self.bundle, 'source', self.root)
        with self.assertRaises(FileExistsError):
            checkpoint.export_run('source', self.bundle, self.root)

    def test_rejects_modified_payload(self):
        damaged = self.root / 'damaged.zip'
        with zipfile.ZipFile(self.bundle) as original, zipfile.ZipFile(damaged, 'w') as target:
            for name in original.namelist():
                target.writestr(name, b'corrupted' if name == 'checkpoint.pt' else original.read(name))
        with self.assertRaisesRegex(ValueError, 'integrity'):
            checkpoint.import_run(damaged, 'damaged', self.root)
        self.assertFalse((self.root / 'results/damaged').exists())

    def test_rejects_unsafe_paths_before_extraction(self):
        damaged = self.root / 'unsafe.zip'
        with zipfile.ZipFile(self.bundle) as original, zipfile.ZipFile(damaged, 'w') as target:
            manifest = json.loads(original.read('bundle.json'))
            manifest['files']['../escape.txt'] = checkpoint.digest(b'escape')
            target.writestr('bundle.json', json.dumps(manifest))
            for name in original.namelist():
                if name != 'bundle.json':
                    target.writestr(name, original.read(name))
            target.writestr('../escape.txt', b'escape')
        with self.assertRaisesRegex(ValueError, 'archive path'):
            checkpoint.import_run(damaged, 'unsafe', self.root)
        self.assertFalse((self.root / 'escape.txt').exists())

    def test_rejects_mixed_unfinished_checkpoint(self):
        (self.root / 'results/source' / checkpoint.BEHAVIOR / 'checkpoint.pt').write_bytes(b'next-update')
        with self.assertRaisesRegex(ValueError, 'still active'):
            checkpoint.export_run('source', self.root / 'mixed.zip', self.root)

    def test_rejects_path_like_run_ids(self):
        for value in ('../other', 'C:drive', '..', 'a/b', 'a\\b'):
            with self.subTest(value=value), self.assertRaises(ValueError):
                checkpoint.import_run(self.bundle, value, self.root)

    def test_torch_zip_prefix_is_not_a_change_of_weights(self):
        def saved(prefix, weights):
            stream = io.BytesIO()
            with zipfile.ZipFile(stream, 'w') as target:
                target.writestr(prefix + '/data.pkl', b'state-dict')
                target.writestr(prefix + '/data/0', weights)
            return stream.getvalue()
        first = saved('checkpoint', b'weights')
        renamed = saved('ArthalionCombatV1-42', b'weights')
        changed = saved('checkpoint', b'new-weights')
        self.assertNotEqual(checkpoint.digest(first), checkpoint.digest(renamed))
        self.assertEqual(checkpoint.torch_records_digest(first), checkpoint.torch_records_digest(renamed))
        self.assertNotEqual(checkpoint.torch_records_digest(first), checkpoint.torch_records_digest(changed))


if __name__ == '__main__':
    unittest.main()
