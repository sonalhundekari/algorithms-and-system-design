# Pattern: Intervals & Greedy

## Key Techniques
- **Sort by start time** — enables linear sweep to detect overlaps
- **Greedy selection** — make locally optimal choice at each step
- **Min-heap** — track active intervals efficiently (e.g. meeting rooms)
- **Ready-queue simulation** — one loop, one key per policy; the clock jumps over idle gaps

## Problems

| Problem | LeetCode | Difficulty | Technique |
|---------|----------|------------|-----------|
| Merge Intervals | #56 | Medium | Sort + linear merge |
| Non-overlapping Intervals | #435 | Medium | Greedy (keep min-end intervals) |
| Meeting Rooms II | #253 | Medium | Min-heap of end times |
| Job Scheduler (FCFS / priority) | OS-scheduling classic | Medium (Hard follow-up) | Ready-queue simulation; SJF/SRTF/round robin; aging |

## Pattern Cheat Sheet

```python
# Merge intervals template
intervals.sort(key=lambda x: x[0])
merged = [intervals[0]]
for start, end in intervals[1:]:
    if start <= merged[-1][1]:
        merged[-1][1] = max(merged[-1][1], end)
    else:
        merged.append([start, end])

# CPU-style job scheduling -- (start, end) is ARRIVAL + DURATION, not an
# occupancy window. One loop covers every policy; only the key changes:
#   FCFS -> arrival   priority -> priority   SJF -> duration   SRTF -> remaining
while scheduled < n:
    admit every job with arrival <= time        # only ARRIVED jobs can be picked
    if not ready: time = next_arrival; continue # idle: jump the clock forward
    job = pop_best(ready)                       # non-preemptive: runs to completion
    emit(job, time, time + job.duration); time += job.duration

# All work-conserving policies share a makespan (busy periods are fixed by the
# input) -- they differ only in WAITING time. SJF is optimal for average wait
# when everything arrives at 0; SRTF is optimal for average turnaround with
# preemption; aging (effective = priority - waited // interval) fixes starvation.
```
