| #   | Isolation      | Order                                              | Waits | Errors & State               | Reserved | Bookings |
|-----|----------------|----------------------------------------------------|-------|------------------------------|----------|----------|
| 1   | read committed | A read, B read, A write + commit, B write + commit |       | None                         | 1        | 2        |
| 1.1 | read committed | A read, B read, A write + commit, B write          |       | 23514 violates "trips_check" | 1        | 1        |
|     |                |                                                    |       |                              |          |          |
|     |                |                                                    |       |                              |          |          |
|     |                |                                                    |       |                              |          |          |
|     |                |                                                    |       |                              |          |          |
|     |                |                                                    |       |                              |          |          |

# 1: Reproduce double booking

- Begin og commit stoppede ikke en dobbelt booking, fordi B læste rækken, før A var committed, og de læste derfor samme
  værdi.
- Read committed stoppede ikke B fra at læse rækken, selvom A havde en igangværende transaktion.
- Unsafe_write skriver derfor ud fra den værdi der blev læst inden A havde committet som var 0, og opdaterer derfor
  rækken
  med 0 + 1 = 1.
- Check constraint accepterer B's commit, fordi checket er på trips rækken, og 1 er en gyldig værdi. Fordi update ikke
  fejler,
  ruller transaktionen ikke tilbage, og indsætter derefter en booking, hvilket resulterer i reserved_seats = 1,
  booking = 2.

## 1.1 Change write to use relative variable

- Efter at tx A er committed, og B forsøger at skrive, bliver der kastet en fejl.
- Det er fordi værdien som B forsøger at skrive nu er relativ. Før skrev B en fast værdi, som var gyldig,
  når B den her gang kører UPDATE, får den et nyt snapshot, med den opdaterede værdi som A skrev, og passer ikke
  check constraint.
- Fordi den fejl aborter hele tx B, så bliver bookingen ikke indsat.

# 2: Use a conditional update

