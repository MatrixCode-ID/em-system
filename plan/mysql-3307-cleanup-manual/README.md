# Retired MySQL 3307 data cleanup

Automatic approval review rejected the agent's recursive removal of the retired 3307 data directory.
The 3307 RunBay app was removed and that server is stopped. The original MySQL instance on port 3306 was
restored and uses a different directory.

Review `remove-3307-data.ps1` before running it. It verifies the resolved path, port 3307 and visible
MySQL processes, then permanently removes the retired instance's data. Run from the em-system root:

```powershell
pwsh -NoLogo -NoProfile -File .\plan\mysql-3307-cleanup-manual\remove-3307-data.ps1
```

The agent did not run this manual script. Expected result: it reports that the retired 3307 directory
was removed. If a check fails, inspect the reported path or process before proceeding.
