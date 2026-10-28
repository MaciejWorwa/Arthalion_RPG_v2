"""Map the Windows process-group stop signal to ML-Agents' normal save-and-exit path."""
import signal
if hasattr(signal, 'SIGBREAK'):
    signal.signal(signal.SIGBREAK, signal.default_int_handler)
from mlagents.trainers.learn import main

if __name__ == '__main__':
    main()
