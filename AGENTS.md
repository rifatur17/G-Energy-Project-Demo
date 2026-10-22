# Project workflow preferences

- Continue this existing G Energy project; preserve other members' commits and uncommitted work.
- The user requests that completed project updates be committed and pushed to GitHub after appropriate verification. Do this as part of finishing each update; do not stop at local edits alone.
- Unless the user specifies otherwise, use `main`, remote `origin` (`https://github.com/rifatur17/G-Energy-Project-Demo`), and the authorized GitHub account `rifatur17`.
- Use repository-local author `Rifatur Rahman <rifaturrahman1131@gmail.com>`. Do not change global Git identity.
- Inspect changes, stage only intended files, and create meaningful commits. Do not create empty commits.
- Fetch and review remote work before pushing. Integrate safely without force-pushing, discarding changes or rewriting shared history. Run checks affected by any integration changes.
- Verify pushed hashes against remote `main`, and report the verified result. If authentication, tests or conflicts block completion, preserve local work and report the exact blocker; never claim a failed/unverified push succeeded.
- Never commit working passwords, SMTP keys, JWTs, user secrets, private credential files, database files/backups, runtime logs or generated build output.
- Use the existing SQL Server development database `localhost / GEnergyDb`. Never drop/reset/recreate it or delete existing records to simplify a task. Apply only reviewed, necessary additive migrations.
