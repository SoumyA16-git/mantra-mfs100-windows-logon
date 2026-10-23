# Gate 1 Biometric Test Results
Date: 2026-10-23 16:31:54
Device: MANTRA MFS100 Serial=49007331 316x354
Overall Gate 1 Status: **PASSED**

## Step Results & Diagnostic Checks
| Step | Result | Detail |
|---|---|---|
| 1.1 SDK Init | PASS | OK |
| 2.1 Diversity | PASS | Unique template (300 bytes) |
| 2.1 Capture (finger1) | PASS | Q=84 NFIQ=1 ISOLen=300 |
| 2.2 Diversity | PASS | Unique template (288 bytes) |
| 2.2 Capture (finger1) | PASS | Q=74 NFIQ=2 ISOLen=288 |
| 2.3 Diversity | PASS | Unique template (294 bytes) |
| 2.3 Capture (finger1) | PASS | Q=77 NFIQ=2 ISOLen=294 |
| 2.4 Diversity | PASS | Unique template (300 bytes) |
| 2.4 Capture (finger1) | PASS | Q=75 NFIQ=2 ISOLen=300 |
| 2.5 Diversity | PASS | Unique template (300 bytes) |
| 2.5 Capture (finger1) | PASS | Q=72 NFIQ=2 ISOLen=300 |
| 3.0 Genuine Matching | PASS | Scores: min=5535 max=10000 mean=8599 count=10 |
| 4.1 Distinctness | PASS | Distinct template bytes (234 bytes) |
| 4.1 Capture (finger2) | PASS | Q=75 NFIQ=2 ISOLen=234 |
| 4.2 Distinctness | PASS | Distinct template bytes (258 bytes) |
| 4.2 Capture (finger2) | PASS | Q=77 NFIQ=2 ISOLen=258 |
| 4.3 Distinctness | PASS | Distinct template bytes (264 bytes) |
| 4.3 Capture (finger2) | PASS | Q=79 NFIQ=2 ISOLen=264 |
| 5.1 Discrimination | PASS | PASS: Separation verified (margin = 5511 points) |
| 5.2 FAR at Threshold 1400 | PASS | PASS: 0.0% False Accepts |
| 6.1 Re-Init after Uninit | PASS | OK |
| 6.2 Capture after Re-Init | PASS | Q=77 NFIQ=2 |

## Genuine Match Scores (Same Finger)
Sample Pairs Evaluated: 10
Min: 5535 | Max: 10000 | Mean: 8599
Observed Pair Scores: 5535, 10000, 8208, 6480, 10000, 10000, 5769, 10000, 10000, 10000

## Impostor Match Scores (Different Fingers)
Sample Pairs Evaluated: 15
Min: 0 | Max: 24 | Mean: 6
Observed Pair Scores: 3, 5, 1, 5, 10, 9, 2, 5, 16, 0, 0, 24, 0, 0, 4

## FAR / FRR Evaluation Table (0–10000 Scale)
| Threshold | FAR% | FRR% | FAR count | FRR count |
|---|---|---|---|---|
| 500 | 0.0% | 0.0% | 0 | 0 |
| 1000 | 0.0% | 0.0% | 0 | 0 |
| 1400 | 0.0% | 0.0% | 0 | 0 |
| 2000 | 0.0% | 0.0% | 0 | 0 |
| 3000 | 0.0% | 0.0% | 0 | 0 |
| 4000 | 0.0% | 0.0% | 0 | 0 |
| 5000 | 0.0% | 0.0% | 0 | 0 |
| 6000 | 0.0% | 20.0% | 0 | 2 |
| 7000 | 0.0% | 30.0% | 0 | 3 |
| 8000 | 0.0% | 30.0% | 0 | 3 |

## Limitations & Privacy Confirmation
- Test fingerprints evaluated solely in volatile memory; zero biometric templates, raw images, or hashes written to persistent storage.
- FAR/FRR statistics reflect the experimental test set of pairwise comparisons and are not certified commercial accuracy ratings.
