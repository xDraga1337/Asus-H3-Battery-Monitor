# v0.0.2 Alpha

Changes since v0.0.1:

- Press R to refresh, A to toggle automatic refresh, and Q to exit without Enter.
- Automatic refresh starts disabled; enabling it queries immediately, then every 5 seconds.
- Manual and automatic queries share one worker, preventing overlapping transactions.
- Refresh errors stay in the menu. Automatic mode retries after headset or USB dongle reconnection; press R to retry when automatic mode is off.
- Improved offline self-tests cover refresh scheduling and report actual failures. All 49 checks pass with exit code 0.
- Distributed as a self-contained, single-file Windows x64 console executable.

Battery percentage estimates remain experimental; voltage accuracy is unverified
and charging detection is unconfirmed. HID packets, transaction order and waits,
voltage parsing, and battery percentage thresholds are unchanged.
