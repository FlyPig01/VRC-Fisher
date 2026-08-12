from time import perf_counter_ns

import numpy as np

from vrc_fisher.capture.latest_frame import LatestFrameSlot
from vrc_fisher.contracts import Frame


def make_frame(sequence: int) -> Frame:
    return Frame(sequence, perf_counter_ns(), np.zeros((2, 2, 3), dtype=np.uint8))


def test_latest_frame_slot_replaces_old_frame() -> None:
    slot = LatestFrameSlot()
    slot.put(make_frame(1))
    slot.put(make_frame(2))

    frame = slot.get()
    assert frame is not None
    assert frame.sequence == 2


def test_wait_for_newer_times_out_without_new_sequence() -> None:
    slot = LatestFrameSlot()
    slot.put(make_frame(2))

    assert slot.wait_for_newer(2, timeout=0.001) is None
