"""Evaluate one frozen ONNX checkpoint; no optimizer or training process is created."""
import argparse
import csv
import json
import hashlib
import signal
from pathlib import Path
import time

import numpy as np
import onnxruntime as ort
from mlagents_envs.base_env import ActionTuple
from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.exception import UnityCommunicatorStoppedException


def model_actions(session, decisions):
    inputs = {}
    for item in session.get_inputs():
        if item.name.startswith('obs_'):
            inputs[item.name] = decisions.obs[int(item.name.split('_')[-1])].astype(np.float32)
        elif item.name == 'action_masks':
            mask = decisions.action_mask
            inputs[item.name] = np.concatenate([~branch for branch in mask], axis=1).astype(np.float32)
        else:
            raise ValueError('Unsupported model input (wrong model/schema?): ' + item.name)
    names = [output.name for output in session.get_outputs()]
    action_name = 'deterministic_discrete_actions' if 'deterministic_discrete_actions' in names else 'discrete_actions'
    if action_name not in names:
        raise ValueError('Model does not export discrete actions: ' + str(names))
    actions = session.run([action_name], inputs)[0].astype(np.int32)
    if actions.ndim == 1:
        actions = actions[:, None]
    if np.any(decisions.action_mask[0][np.arange(len(actions)), actions[:, 0]]):
        raise ValueError('The frozen model returned a masked action.')
    return ActionTuple(discrete=actions)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--env', type=Path, required=True)
    p.add_argument('--model', type=Path, required=True)
    p.add_argument('--scenarios', default='AIlearning')
    p.add_argument('--episodes', type=int, default=400)
    p.add_argument('--seed', type=int, default=87654321)
    p.add_argument('--port', type=int, default=6005)
    p.add_argument('--output', type=Path, default=Path('Tools/AI/metrics/evaluation'))
    p.add_argument('--visible', action='store_true')
    args = p.parse_args()
    if hasattr(signal, 'SIGBREAK'):
        signal.signal(signal.SIGBREAK, signal.default_int_handler)
    if args.episodes < 1:
        raise SystemExit('episodes must be positive.')
    metadata_path = args.model.with_suffix('.metadata.json')
    if not metadata_path.exists():
        raise SystemExit('Model metadata is missing: ' + str(metadata_path))
    metadata = json.loads(metadata_path.read_text(encoding='utf-8-sig'))
    digest = hashlib.sha256(args.model.read_bytes()).hexdigest()
    if metadata.get('model_sha256', digest) != digest:
        raise SystemExit('Model file does not match its metadata hash.')
    options = ort.SessionOptions()
    options.intra_op_num_threads = 2
    session = ort.InferenceSession(str(args.model.resolve()), options, providers=['CPUExecutionProvider'])
    args.output.mkdir(parents=True, exist_ok=True)
    extra = ['--ai-mode', 'Evaluation', '--ai-external-policy', 'true', '--ai-scenarios', args.scenarios,
             '--ai-seed', str(args.seed), '--ai-episodes', str(args.episodes), '--ai-output', str(args.output.resolve()),
             '--ai-fast', 'false' if args.visible else 'true', '--ai-model-label', args.model.name + ':' + digest[:12]]
    env = UnityEnvironment(file_name=str(args.env.resolve()), base_port=args.port, seed=args.seed,
                           no_graphics=not args.visible, timeout_wait=120, additional_args=extra)
    try:
        env.reset()
        manifests = list(args.output.glob('worker_*/manifest.json'))
        if not manifests:
            raise RuntimeError('Environment did not write its observation manifest.')
        current = json.loads(manifests[0].read_text(encoding='utf-8-sig'))
        for key in ('schema', 'rules'):
            if current.get(key) != metadata.get(key):
                raise RuntimeError('Checkpoint/environment mismatch: ' + key)
        while True:
            for behavior in env.behavior_specs:
                decisions, _ = env.get_steps(behavior)
                if len(decisions):
                    env.set_actions(behavior, model_actions(session, decisions))
            env.step()
    except UnityCommunicatorStoppedException:
        files = list(args.output.glob('worker_*/episodes.csv'))
        rows = []
        for file in files:
            with file.open(encoding='utf-8-sig', newline='') as stream:
                rows.extend(csv.DictReader(stream, delimiter=';'))
        if len(rows) < args.episodes:
            raise RuntimeError('Evaluation ended early: ' + str(len(rows)) + ' episodes.')
        print(json.dumps({'model': args.model.name, 'episodes': len(rows), 'last_result': rows[-1]}, indent=2))
    finally:
        env.close()


if __name__ == '__main__':
    main()
