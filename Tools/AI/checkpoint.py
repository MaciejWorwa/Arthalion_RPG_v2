"""Transfer a stopped PPO run, including optimizer state, between computers.

Only import bundles from a trusted source: PyTorch loads checkpoint.pt when training
resumes. This utility verifies ZIP paths and hashes without unpickling the checkpoint.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import shutil
import tempfile
import zipfile

HERE = Path(__file__).resolve().parent
BEHAVIOR = 'ArthalionCombatV1'
REQUIRED = {'checkpoint.pt', 'policy.onnx', 'policy.metadata.json', 'trainer.yaml'}


def valid_run_id(value):
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_-]{0,79}', value):
        raise ValueError('RunId: use 1-80 ASCII letters, digits, hyphens or underscores.')
    return value


def allowed_member(name):
    return name in REQUIRED or bool(re.fullmatch(
        r'events/events\.out\.tfevents\.[A-Za-z0-9_.-]+|metrics/worker_[0-9]+/(episodes\.csv|manifest\.json)', name))


def digest(data):
    return hashlib.sha256(data).hexdigest()


def torch_records_digest(data):
    # torch.save uses a ZIP prefix derived from the filename: checkpoint.pt and
    # Behavior-step.pt differ as ZIP files even when every tensor is identical.
    stream = io.BytesIO(data)
    if not zipfile.is_zipfile(stream):
        return digest(data)
    with zipfile.ZipFile(stream) as saved:
        records = sorted((name.partition('/')[2], saved.read(name)) for name in saved.namelist())
    value = hashlib.sha256()
    for name, content in records:
        value.update(name.encode('utf-8') + b'\0' + len(content).to_bytes(8, 'little') + content)
    return value.hexdigest()


def export_run(run_id, output, root=HERE):
    valid_run_id(run_id)
    run = root / 'results' / run_id
    metrics = root / 'metrics' / run_id
    status = json.loads((run / 'run_logs/training_status.json').read_text(encoding='utf-8-sig'))
    latest = status[BEHAVIOR]['final_checkpoint']
    payload = {
        'checkpoint.pt': (run / BEHAVIOR / 'checkpoint.pt').read_bytes(),
        'policy.onnx': (run / (BEHAVIOR + '.onnx')).read_bytes(),
        'policy.metadata.json': (run / (BEHAVIOR + '.metadata.json')).read_bytes(),
        'trainer.yaml': (metrics / 'trainer.yaml').read_bytes(),
    }
    # A completed export must refer to the same weights as the latest saved step.
    step_file = run / BEHAVIOR / f'{BEHAVIOR}-{latest["steps"]}.pt'
    if torch_records_digest(payload['checkpoint.pt']) != torch_records_digest(step_file.read_bytes()):
        raise ValueError('Checkpoint changed or run is still active. Stop training before export.')
    if digest(payload['policy.onnx']) != digest(step_file.with_suffix('.onnx').read_bytes()):
        raise ValueError('Final ONNX is not the latest checkpoint. Stop training before export.')
    metadata = json.loads(payload['policy.metadata.json'].decode('utf-8-sig'))
    if metadata.get('model_sha256') != digest(payload['policy.onnx']):
        raise ValueError('ONNX does not match its metadata. Stop the training runner first.')
    for source in (run / BEHAVIOR).glob('events.out.tfevents.*'):
        payload['events/' + source.name] = source.read_bytes()
    for source in metrics.glob('worker_*/*'):
        name = 'metrics/' + source.parent.name + '/' + source.name
        if source.is_file() and allowed_member(name):
            payload[name] = source.read_bytes()
    if any(not allowed_member(name) for name in payload):
        raise ValueError('Unsupported file name in this run.')
    manifest = {
        'format': 1, 'run_id': run_id, 'behavior': BEHAVIOR,
        'steps': int(latest['steps']), 'reward': latest.get('reward'),
        'creation_time': latest['creation_time'], 'trainer_metadata': status['metadata'],
        'files': {name: digest(data) for name, data in payload.items()},
    }
    output = Path(output).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    # Exclusive creation protects a previously exported checkpoint.
    with output.open('xb') as stream, zipfile.ZipFile(stream, 'w', zipfile.ZIP_DEFLATED) as bundle:
        bundle.writestr('bundle.json', json.dumps(manifest, indent=2))
        for name, data in payload.items():
            bundle.writestr(name, data)
    return manifest


def import_run(bundle_path, run_id, root=HERE):
    valid_run_id(run_id)
    root = Path(root).resolve()
    target = root / 'results' / run_id
    target_metrics = root / 'metrics' / run_id
    if target.exists() or target_metrics.exists():
        raise ValueError('RunId already exists. Choose a new RunId; existing runs are never overwritten.')
    with zipfile.ZipFile(bundle_path) as bundle:
        members = bundle.infolist()
        names = [item.filename for item in members]
        if len(names) != len(set(names)) or 'bundle.json' not in names:
            raise ValueError('Duplicate entries or missing bundle manifest.')
        if any(item.file_size > 256 * 1024**2 for item in members) or sum(item.file_size for item in members) > 1024**3:
            raise ValueError('Bundle exceeds the supported size limit.')
        manifest = json.loads(bundle.read('bundle.json'))
        if manifest.get('format') != 1 or manifest.get('behavior') != BEHAVIOR:
            raise ValueError('Unsupported checkpoint format or behavior.')
        files = manifest['files']
        if not REQUIRED.issubset(files) or set(names) != set(files) | {'bundle.json'}:
            raise ValueError('Missing or unexpected checkpoint files.')
        if any(not allowed_member(name) for name in files):
            raise ValueError('Unsafe or unsupported archive path.')
        payload = {name: bundle.read(name) for name in files}
        if any(digest(data) != files[name] for name, data in payload.items()):
            raise ValueError('Checkpoint integrity check failed (SHA-256).')
    step = manifest['steps']
    if type(step) is not int or step < 0:
        raise ValueError('Invalid checkpoint step.')
    metadata = json.loads(payload['policy.metadata.json'].decode('utf-8-sig'))
    if metadata.get('model_sha256') != digest(payload['policy.onnx']):
        raise ValueError('ONNX does not match the model metadata.')
    target.parent.mkdir(parents=True, exist_ok=True)
    target_metrics.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='checkpoint-import-', dir=target.parent) as temporary:
        staging = Path(temporary) / 'run'
        staging_metrics = Path(temporary) / 'metrics'
        (staging / BEHAVIOR).mkdir(parents=True)
        (staging / 'run_logs').mkdir()
        staging_metrics.mkdir()
        destinations = {
            'checkpoint.pt': staging / BEHAVIOR / 'checkpoint.pt',
            'policy.onnx': staging / (BEHAVIOR + '.onnx'),
            'policy.metadata.json': staging / (BEHAVIOR + '.metadata.json'),
            'trainer.yaml': staging_metrics / 'trainer.yaml',
        }
        for name, data in payload.items():
            if name.startswith('events/'):
                destination = staging / BEHAVIOR / name.split('/')[1]
            elif name.startswith('metrics/'):
                destination = staging_metrics / Path(name).relative_to('metrics')
            else:
                destination = destinations[name]
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(data)
        step_name = f'{BEHAVIOR}-{step}'
        shutil.copyfile(staging / BEHAVIOR / 'checkpoint.pt', staging / BEHAVIOR / (step_name + '.pt'))
        shutil.copyfile(staging / (BEHAVIOR + '.onnx'), staging / BEHAVIOR / (step_name + '.onnx'))
        shutil.copyfile(staging / (BEHAVIOR + '.metadata.json'), staging / BEHAVIOR / (step_name + '.metadata.json'))
        # ML-Agents stores absolute paths in this index: rebuild them for this PC.
        entry = dict(steps=step, reward=manifest['reward'], creation_time=manifest['creation_time'],
                     file_path=str(target / BEHAVIOR / (step_name + '.onnx')),
                     auxillary_file_paths=[str(target / BEHAVIOR / (step_name + '.pt'))])
        final = {**entry, 'file_path': str(target / (BEHAVIOR + '.onnx'))}
        status = {BEHAVIOR: {'checkpoints': [entry], 'final_checkpoint': final},
                  'metadata': manifest['trainer_metadata']}
        (staging / 'run_logs/training_status.json').write_text(json.dumps(status, indent=2), encoding='utf-8')
        staging.rename(target)
        staging_metrics.rename(target_metrics)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    export = commands.add_parser('export', help='Export a stopped run; never run during training.')
    export.add_argument('--run-id', required=True)
    export.add_argument('--output', type=Path, required=True)
    load = commands.add_parser('import', help='Import under a new, unused RunId.')
    load.add_argument('--bundle', type=Path, required=True)
    load.add_argument('--run-id', required=True)
    args = parser.parse_args()
    try:
        manifest = (export_run(args.run_id, args.output) if args.command == 'export'
                    else import_run(args.bundle, args.run_id))
    except (ValueError, OSError, KeyError, zipfile.BadZipFile) as error:
        raise SystemExit(str(error)) from error
    print(f'{args.command}: {args.run_id}; saved step={manifest["steps"]}; optimizer + ONNX + metrics + TensorBoard.')
    if args.command == 'import':
        print(f'Continue: .\\Tools\\AI\\start-training.ps1 -RunId {args.run_id} -Resume -Minutes 60')


if __name__ == '__main__':
    main()
