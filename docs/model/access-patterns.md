# Access patterns

| # | Workload | Aktør | Read/Write | Latency | Consistency |
|---|----------|-------|------------|---------|-------------|
| 1 | Journey Search | Kunde | Read heavy | Lav | Stale acceptabelt |
| 2 | Ticket Purchase | Kunde | Read/Write | Sekunder | Strong/Critical |
| 3 | Ticket Validation | Kunde | Read/Write | Lav | Strong |
| 4 | Timetable Maintenance | Operatør | Write | N/A | Eventual |
| 5 | Real-time Availability | Begge | Read heavy, Write sjældent | Lav | Godt nok til søgning, og pålideligt nok til køb |
| 6 | Reporting | Operatør | Read | Minutter? | Eventual |
