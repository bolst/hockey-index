# Data retention

| Data | Kept | Removed |
|---|---|---|
| Event listings | Until 12 months after the event ends | A purge job deletes them |
| Host account (phone, optional email) | While the account exists | Admin deletes on request by email |
| OTP and rate-limit state | Short term | Expires with the limiter windows |
| Reports | With the event | Deleted with the event |
| Audit log | Indefinitely | Rows keep no PII after account deletion |
| Backups (encrypted) | 30 days daily, 12 weeks weekly | R2 lifecycle and `backup.sh` retention |
| Logs | Grafana Cloud free tier retention | Automatic |

Restore drill: the drill decrypts production data to check backups. Record the runner choice (hosted with controls, self-hosted, or laptop) in [the runbook](../runbook.md#restore) and keep this page in step.

Publish a user-facing privacy page before launch. Review PIPEDA and CCPA obligations, including self-service deletion (open question Q-new-3).
