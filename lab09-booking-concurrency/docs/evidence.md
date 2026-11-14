| #   | Isolation       | Order                                                              | Waits                | Errors & State                                                                | Reserved | Bookings |
|-----|-----------------|--------------------------------------------------------------------|----------------------|-------------------------------------------------------------------------------|----------|----------|
| 1   | read committed  | A read, B read, A write + commit, B write + commit                 | None                 | None                                                                          | 1        | 2        |
| 1.1 | read committed  | A read, B read, A write + commit, B write                          | None                 | 23514 violates "trips_check"                                                  | 1        | 1        |
| 2   | read committed  | A read, B read, A update, b update, A commit, B commit             | B waits              | Så snart A committer, returnerer B INSERT 0 0                                 | 1        | 1        |
| 2.1 | read committed  | A begin + book, B begin + book, A commit, B rollback               | B waits              | Når a committer giver B: ERROR:  P0001: No seat available on trip T-LAST      | 1        | 1        |
| 2.2 | read committed  | A begin + book, B begin + book, A rollback, B commit               | B waits              | Når a aborter, får B en returværdi, og er klar til at committe.               | 1        | 1        |
| 3.1 | read committed  | A read + lock, B read, A book, A commit                            | B waits after read   | Når a committer, returnere B No seat available on trip                        | 1        | 1        |
| 3.2 | read committed  | A read + lock, B read, A book + rollback, B book                   | None                 | B venter ikke på A, begge booker                                              | 1        | 1        |
| 3.3 | read committed  | A read, B read, A commit, B commit                                 | B                    | B venter på A, selvom tx forsøger at booke et andet trip                      | 2        | 2        |
| 3.4 | read committed  | A read, B read, A rollback, B commit                               | B                    | B venter på A som i 3.3                                                       | 1        | 1        |
| 3.5 | read committed  | A begin, A book, B unsafe read, B unsafe write, A commit, B commit | B                    | B venter nu ikke med at læse, men venter stadig på at kunne update            | 1        | 2        |
| 3.6 | read committed  | A T-LAST, B T-OTHER, A T-OTHER, B T-LAST                           | A                    | ERROR:  40P01: deadlock detected                                              |          |          |
| 4.1 | read committed  | A read, B read, A write, A commit, B write, B rollback             | B                    | Reservation failed: trip values have changed                                  | 1        | 1        |
| 4.2 | read committed  | B rollback, B read, B write, B commit                              |                      | B læser værdien igen og kan booke                                             | 2        | 2        |
| 4.3 | read committed  | A read, B read, A write, B write, A rollback, B commit             | B waits before write | B venter på A slipper ved roillback                                           | 1        | 1        |
| 5.1 | repeatable read | A read, B read, A write, A commit, B write                         | None                 | ERROR: 40001: could not serialize access due to concurrent update             | 1        | 1        |
| 5.2 | repeatable read | B rollback, B read                                                 | None                 | Reserved: 1, can_book: f                                                      | 1        | 1        |
| 5.3 | repeatable read | A read, B read, A write, commit, B write, B retry write            | B                    | Først 40001, booker ved retry                                                 | 1        | 2        |
| 5.4 | serializable    | A read, B read, A write, commit, B write, rollback, read, write    | None                 | Først 40001, retry: The read found no available seat. No booking was written. | 1        | 1        |
|     |                 |                                                                    |                      |                                                                               |          |          |
|     |                 |                                                                    |                      |                                                                               |          |          |
|     |                 |                                                                    |                      |                                                                               |          |          |
|     |                 |                                                                    |                      |                                                                               |          |          |
|     |                 |                                                                    |                      |                                                                               |          |          |
|     |                 |                                                                    |                      |                                                                               |          |          |

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

- Efter at A har kørt booking, bliver B kørt med unsafe-read+write. B kan nu læse rækken, men kan ikke skrive før A
  slipper row-lock på T-LAST,
  så snart A har sluppet, indsætter B booking, og der sker en dobbeltbooking. B tog heller aldrig advisory lock.
- B ventede altså på A, men fik sin værdi fra den unsafe-read, hvilket var seen_reserved +1, hvilket var 0, fordi read
  ikke ventede på A var færdig.
- Advisory lock kan ikke beskytte mod kode der ikke tager låsen.

# 3.6: Deadlock

- A forsøger først af få låsen på den række B har row-lock på, herefter forsøger B at få den row som A har.

# 4: Detect a stale read with an optimistic read

## 4.1: Optimistic

- Selvom der var et sæde tilbage, da B forsøgte at write, blev tx afvist med "values have changed", fordi
  implementeringen kigger på, om den nuværende værdi af reserved_seats var den samme, som blev læst til at starte med.
  Derfor kan den nuværende implementation af optimistisk ikke fortælle os om der er flere sæder, men bare at noget har
  ændret sig.
- Vi kan dog forsøge at håndtere det scenarie ved at forsøge et retry, såfremt fejlen ikke er, at der ikke er flere
  pladser.

## 4.2 Optimistic: Rollback B, try again

- B læser reserved seats igen og får 1, herefter lykkedes B's update.

## 4.3 Optimistic: Rollback A

- Opfører sig på samme måde som conditional update fra opgave 2, så der er stadig en row-lock i spil her.
- Med udgangspunkt i T-TWO, så vil conditional stadig booke med tx B, efter A har frigivet låsen. I denne version kræver
  det et
  retry, fordi tx B rammer 0 rækker: "values have changed".

## Version column

- Forskellen på count check og version kan ses hvis vi inddrager en tredje tx C som returnerer en billet mellem A og B.
- Hvis der er 1 billet tilbage, ud af 2 pladser og A booker den sidste, mens C og B venter på at kunne skrive. Efter A
  så returnerer
  C en billet, hvorefter B forsøger at booke. Med count, ville tx B få lov at booke, fordi der er en ledig billet, med
  version og optimistisk tjek,
  ville B kræve et retry, fordi versions nummeret er blevet incremented.

# 5: Compare isolation levels

## 5.1: Unsafe with repeatable read

- Read committed tager den nyeste committede række og fortsætter, mens repeatable read nægter og giver 40001.
- I opgave 1 gik B's update bare igennem. Repeatable read bruger et snapshot for hele
  transaktionen.

## 5.3: Retry only write

- Hvis tx B kun retrier write, er dobbeltbookingen tilbage. Det er nødvendigt at retry hele transaktionen, altså både
  read og write.

## 5.4: Serializable

- Første gang B forsøger at write får den ERROR: 40001, fordi noget er blevet modificeret imens.
- Efter retry af hele transaktionen får B: The read found no available seat. No booking was written, fordi read nu
  aflæser den
  korrekte værdi, og write derfor agerer ud fra den, i stedet for en forældet snapshot.

- RR og Ser. opfører sig ens her, men i RR er Serialization Anomaly stadig en mulighed. 
- Ifølge docs opfører de sig også ens, på nær ekstra monitorering ift. den nævnte anomali. 

# Does protecting this shared counter prove that all multi-row business rules are safe?

- Nej, repeatable read vil beskytte mod at to transaktioner incrementer counter. Hvis vi har en regel der hedder, at én passager, ikke
  kan have to bookinger til hver sin afgang, hvis de ligger i samme tidsrum, beskytter RR ikke. Fordi de træffer en beslutning ud fra den samme read,
  men skriver til forskellige rækker.

- RR beskytter counter, fordi begge tx skriver til samme række, og RR opdaterer ikke en række, der er ændret efter snapshottet. 
  Så snapshottet beskytter ikke noget.
- Ser. sørger for at det der blev læst, ikke blev ændret af en anden tx, sådan at det ikke ville give samme resultat, at køre det en efter en -
  for hvis det blev kørt en efter en, ville B se at A havde indsat en booking, før B læste bookings.
- Så hvis de begge havde læst en passagers bookinger, og ud fra det, hver skriver en booking, på hver sin rute, vil det give en fejl,
  fordi det som der blev læst, nu er ændret. 

# 40001 for each level

- Read committed: Det er kun RR og Ser. der får 40001, fordi RC kun læser det nyeste.
  RR holder styr på writes, Ser. både reads og writes.
- Repeatable read: Her ville jeg gentage hele tx, men ikke med payment, medmindre payments havde en idempotence key, som payment provider håndterede.
  antal retries kunne være 3, så vi har en buffer på et ekstra forsøg. Brugeren burde ikke se andet end en loading bar, da det er implementerings deltaljer.
  Hvis alle forsøg fejler, ville brugeren modtage en besked med at noget gik galt med booking, og at de skal prøve igen.
  Hvis betalingen lykkedes inde i tx, men bookingen fejler efter, skal der være et fallback/final hvor betaling returneres, men først efter at bookingen har brugt sine retries.
- Hvis betaling ligger i loopet, uden idempotence key, vil der i værste fald blive trukket penge 3 gange, hvilke rollback ikke kan gøre noget ved.
- Serializable: Samme som RR.