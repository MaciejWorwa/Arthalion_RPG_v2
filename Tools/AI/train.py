"""Bounded local training plus independent, frozen-checkpoint evaluation."""
import argparse
import json
import hashlib
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import time

import yaml

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]


def arguments():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--run-id', required=True)
    p.add_argument('--env', type=Path, default=ROOT / 'Builds/AI/ArthalionAI.exe')
    p.add_argument('--scenarios', default='@starter')
    p.add_argument('--num-envs', type=int, default=1)
    p.add_argument('--minutes', type=float, default=60)
    p.add_argument('--steps', type=int, default=500000)
    p.add_argument('--seed', type=int, default=12345)
    p.add_argument('--device', default='cpu')
    p.add_argument('--resume', action='store_true')
    p.add_argument('--self-play', action='store_true')
    p.add_argument('--editor', action='store_true')
    p.add_argument('--eval-every-minutes', type=float, default=10)
    p.add_argument('--eval-episodes', type=int, default=40)
    p.add_argument('--base-port', type=int, default=5005)
    return p.parse_args()


def stop(process):
    if process is None or process.poll() is not None:
        return
    if os.name == 'nt':
        process.send_signal(signal.CTRL_BREAK_EVENT)
    else:
        process.send_signal(signal.SIGINT)
    try:
        process.wait(timeout=45)  # Give ML-Agents time to save its checkpoint and ONNX.
    except subprocess.TimeoutExpired:
        process.terminate()
        process.wait(timeout=15)


def checkpoint_metadata(metrics, model):
    manifests = sorted(metrics.glob('worker_*/manifest.json'))
    if not manifests:
        return
    manifest = json.loads(manifests[0].read_text(encoding='utf-8-sig'))
    manifest['model_sha256'] = hashlib.sha256(model.read_bytes()).hexdigest()
    model.with_suffix('.metadata.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')


def main():
    args = arguments()
    if args.num_envs < 1 or args.steps < 1 or args.minutes <= 0:
        raise SystemExit('num-envs, steps and minutes must be positive.')
    if Path(args.run_id).name != args.run_id or args.run_id in ('.', '..'):
        raise SystemExit('run-id must be a directory name, not a path.')
    if not args.editor and not args.env.is_file():
        raise SystemExit('Build the training player using Arthalion > AI > Build training player first.')
    result_root = HERE / 'results'
    metrics = HERE / 'metrics' / args.run_id
    metrics.mkdir(parents=True, exist_ok=True)
    config = yaml.safe_load((HERE / ('selfplay.yaml' if args.self_play else 'ppo.yaml')).read_text())
    config['behaviors']['ArthalionCombatV1']['max_steps'] = args.steps
    config_path = metrics / 'trainer.yaml'
    if args.resume and config_path.exists():
        previous = yaml.safe_load(config_path.read_text())
        if previous['behaviors']['ArthalionCombatV1']['network_settings'] != config['behaviors']['ArthalionCombatV1']['network_settings']:
            raise SystemExit('Refusing to resume with a different network architecture.')
    config_path.write_text(yaml.safe_dump(config, sort_keys=False), encoding='utf-8')
    command = [sys.executable, str(HERE / 'learn.py'), str(config_path), '--run-id', args.run_id,
               '--results-dir', str(result_root), '--seed', str(args.seed), '--torch-device', args.device,
               '--base-port', str(args.base_port), '--num-envs', str(args.num_envs)]
    if args.resume:
        command.append('--resume')
    if not args.editor:
        command.extend(['--env', str(args.env), '--no-graphics'])
    command.extend(['--env-args', '--ai-mode', 'Training', '--ai-scenarios', args.scenarios,
                    '--ai-seed', str(args.seed), '--ai-fast', 'true', '--ai-output', str(metrics),
                    '--ai-model-label', args.run_id, '--ai-opponent', 'SelfPlay' if args.self_play else 'AutoCombat'])
    environment = os.environ.copy()
    environment.setdefault('OMP_NUM_THREADS', str(max(1, min(4, (os.cpu_count() or 4) // args.num_envs))))
    flags = subprocess.CREATE_NEW_PROCESS_GROUP if os.name == 'nt' else 0
    print('Training budget:', args.minutes, 'minutes;', args.steps, 'policy decisions;', args.num_envs, 'environment(s).', flush=True)
    print('Metrics:', metrics, '\nTensorBoard: Tools/AI/.venv/Scripts/tensorboard.exe --logdir Tools/AI/results', flush=True)
    if args.editor:
        print('In BattleScene select Training, then Play. Editor settings select the roster and opponent.', flush=True)
    trainer = subprocess.Popen(command, cwd=ROOT, env=environment, creationflags=flags)
    evaluator = None
    start = last_eval = time.monotonic()
    last_checkpoint = None
    try:
        while trainer.poll() is None:
            now = time.monotonic()
            if now - start >= args.minutes * 60:
                print('Time budget reached; saving and stopping.', flush=True)
                break
            if not args.editor and args.eval_every_minutes > 0 and now - last_eval >= args.eval_every_minutes * 60 and (evaluator is None or evaluator.poll() is not None):
                models = sorted((result_root / args.run_id).rglob('*.onnx'), key=lambda p: p.stat().st_mtime)
                if models:
                    source = models[-1]
                    identity = (str(source), source.stat().st_mtime_ns)
                    if identity != last_checkpoint and time.time() - source.stat().st_mtime > 3:
                        evaluation = metrics / ('evaluation_' + str(int(now - start)))
                        evaluation.mkdir()
                        model = evaluation / source.name
                        shutil.copy2(source, model)
                        checkpoint_metadata(metrics, model)
                        evaluator = subprocess.Popen([sys.executable, str(HERE / 'evaluate.py'), '--env', str(args.env),
                            '--model', str(model), '--scenarios', args.scenarios, '--episodes', str(args.eval_episodes),
                            '--output', str(evaluation), '--port', str(args.base_port + 1000)], cwd=ROOT, creationflags=flags)
                        last_checkpoint = identity
                last_eval = now
            time.sleep(1)
    except KeyboardInterrupt:
        print('Stopping and saving the training checkpoint.', flush=True)
    finally:
        stop(trainer)
        stop(evaluator)
        for model in (result_root / args.run_id).rglob('*.onnx'):
            checkpoint_metadata(metrics, model)
    return 0 if trainer.returncode in (0, None, 130, -signal.SIGINT, -1073741510) else trainer.returncode


if __name__ == '__main__':
    raise SystemExit(main())
