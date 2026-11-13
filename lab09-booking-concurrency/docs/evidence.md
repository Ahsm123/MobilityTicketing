| #   | Isolation      | Order                                                              | Waits              | Errors & State                                                           | Reserved | Bookings |
|-----|----------------|--------------------------------------------------------------------|--------------------|--------------------------------------------------------------------------|----------|----------|
| 1   | read committed | A read, B read, A write + commit, B write + commit                 | None               | None                                                                     | 1        | 2        |
| 1.1 | read committed | A read, B read, A write + commit, B write                          | None               | 23514 violates "trips_check"                                             | 1        | 1        |
| 2   | read committed | A read, B read, A update, b update, A commit, B commit             | B waits            | Så snart A committer, returnerer B INSERT 0 0                            | 1        | 1        |
| 2.1 | read committed | A begin + book, B begin + book, A commit, B rollback               | B waits            | Når a committer giver B: ERROR:  P0001: No seat available on trip T-LAST | 1        | 1        |
| 2.2 | read committed | A begin + book, B begin + book, A rollback, B commit               | B waits            | Når a aborter, får B en returværdi, og er klar til at committe.          | 1        | 1        |
| 3.1 | read committed | A read + lock, B read, A book, A commit                            | B waits after read | Når a committer, returnere B No seat available on trip                   | 1        | 1        |
| 3.2 | read committed | A read + lock, B read, A book + rollback, B book                   | None               | B venter ikke på A, begge booker                                         | 1        | 1        |
| 3.3 | read committed | A read, B read, A commit, B commit                                 | B                  | B venter på A, selvom tx forsøger at booke et andet trip                 | 2        | 2        |
| 3.4 | read committed | A read, B read, A rollback, B commit                               | B                  | B venter på A som i 3.3                                                  | 1        | 1        |
| 3.5 | read committed | A begin, A book, B unsafe read, B unsafe write, A commit, B commit | B                  | B venter nu ikke med at læse, men venter stadig på at kunne update       | 1        | 2        |
| 3.6 | read committed | A T-LAST, B T-OTHER, A T-OTHER, B T-LAST                           | A                  | ERROR:  40P01: deadlock detected                                  |          |          |
|     |                |                                                                    |                    |                                                                          |          |          |
|     |                |                                                                    |                    |                                                                          |          |          |
|     |                |                                                                    |                    |                                                                          |          |          |
|     |                |                                                                    |                    |                                                                          |          |          |
|     |                |                                                                    |                    |                                                                          |          |          |

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

## 1.1: Change write to use relative variable

- Efter at tx A er committed, og B forsøger at skrive, bliver der kastet en fejl.
- Det er fordi værdien som B forsøger at skrive nu er relativ. Før skrev B en fast værdi, som var gyldig,
  når B den her gang kører UPDATE, får den et nyt snapshot, med den opdaterede værdi som A skrev, og passer ikke
  check constraint.
- Fordi den fejl aborter hele tx B, så bliver bookingen ikke indsat.

# 2: Use a conditional update

- Med 02_conditional-update.sql, venter B med at køre sin update, indtil A har committet, fordi da A startede update
  tog den en row-lock på rækken. Så snart A blev committet, returnerede B INSERT 0 0, og indsatte ikke en booking.
- Det sker fordi at efter A har committed, tjekker B sin WHERE clause igen mod den nye række, som A har committet,
  hvilket betyder at reserved_seats < capacity nu er false, så update rammer 0 rækker, hvilket betyder at seat er tom,
  og insert indsætter 0 rækker.
- Så forskellen fra 1.1, er at B's transaktion stadig går igennem, den ændrer bare ikke noget, hvor den før gav fejl.

## 2.1 + 2.2:

- Når A kører book, tager tx en row-lock, hvorefter B book afventer. Når A committer, returnerer B's update 0 rækker og
  kaster en
  error.
- Samme eksperiment, men hvor A laver en rollback efter book, B som ventede bliver frigivet efter og B's update
  returnerer 1 row, hvorefter der kan
  committes. Igen er det fordi B tjekker sin WHERE igen, når den eksekverer, og der er tx A rullet tilbage.

- Statement der træffer beslutning om kapacitet er updates where clause.
- Det er ike nok at læse antal af ledige pladser først, som det kan ses af eksperiment 1, fordi capacity så bliver
  udregnet
  ud fra den gamle værdi.
- Conditional update skal stadig vente, som det kan ses i eksperiment 2, 2.1, 2.2, hvor B stadig venter.
- At update returnerer 0 rækker, fortæller ikke hvad der skete præcist, men bare at der ikke var en række der matchede,
  og at der derfor ikke blev ændret noget.

# 3: Compare explicit locks

# 3.1: Row-lock same trip - 03-rowlock.sql

- Når A starter med at læse rækken, og B læser lige efter, bliver B stoppet, fordi A tager en rowlock via FOR UPDATE
  efter SELECT.
- Efter A har booket og committet, returnere B med: No seat available on trip.
- Andre passagerer der prøver at booke en sæde på samme trip skal vente, indtil første tx har sluppet sin lås.

# 3.2: Row-lock different trip - 03-rowlock.sql

- A låser kun T-LAST, B kan godt booke T-OTHER.

# 3.3: Lock-table - A commit

- A låser hele trips tabellen, og B venter på table-lock, hvilket betyder at vi helt mister concurrent bookings, selvom
  det er forskellige trips.
- Table-lock betyder at alle bookinger vil køre sekventielt.

# 3.4: Lock-table - A rollback

- Samme som 3.3 - A låser rækken, B venter. Når A laver rollback eksekverer B.

# 3.5: Advisory-lock - A commit

- Efter at A har kørt booking, bliver B kørt med unsafe-read+write. B kan nu læse rækken, men kan ikke skrive før A slipper row-lock på T-LAST,
  så snart A har sluppet, indsætter B booking, og der sker en dobbeltbooking. B tog heller aldrig advisory lock. 
- B ventede altså på A, men fik sin værdi fra den unsafe-read, hvilket var seen_reserved +1, hvilket var 0, fordi read ikke ventede på A var færdig.
- Advisory lock kan ikke beskytte mod kode der ikke tager låsen.

# 3.6: Deadlock
- A forsøger først af få låsen på den række B har row-lock på, herefter forsøger B at få den row som A har.
