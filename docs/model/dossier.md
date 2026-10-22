# Antagelser:

1. En rute kan indeholde det samme stop flere gange, men ikke det samme to gange i træk.
2. På bus og letbane kan en kunde validere en billet ved påstigning, og i et tog kan en kontrollør validere on demand.

# Funktionel afhængighed

1. route_id + stop_sequence -> stop_id.
   Fordi vi kan bestemme et stop_id ud fra sekvensnummeret på en rute, men ikke omvendt. Et sekvensnummer kan ikke udledes
   af route_id + stop_id, fordi vi antager, at et stop godt kan optræde flere gange på en rute.
   Det vil sige at {x, y} > z gælder, men {x, z} > y gælder ikke.

   Normalisering skal forhindre at en kolonne som ikke er en nøgle, er afhængig af en anden kolonne, som heller ikke er en nøgle.
   Hvis både stop_name og stop_id var i route_stops, så ville stop_name være afhængig af stop_id, og stop_name
   ville også blive gentaget i alle rækker i route_stops.

# Regler
Reglerne er markeret fra A-D, som beskriver hvor de kan håndhæves.
- A: True/False og kan valideres direkte på rækken med et CHECK eller NOT NULL.
- B: Kan valideres i samme tabel med UNIQUE eller UNIQUE INDEX.
- C: Kan valideres i en anden tabel med FOREIGN KEY.
- D: Validering kræver data som ikke er tilgængelige i databasen, og skal valideres i applikationen.

1. Antallet af pladser kan ikke være negativ.
   - A: Kan håndhæves med CHECK (capacity >= 0).

2. Antal reserverede pladser kan ikke være negativt eller større end antal pladser.
   - A: Kan håndhæves med CHECK (reserved >= 0 AND reserved <= capacity).

3. Priser og betalingsbeløb kan ikke være negative.
   - A: Kan håndhæves med CHECK (price >= 0) og CHECK (amount >= 0).

4. En billet skal have en reference til en eksisterende bruger, tur og et produkt.
   - A: Kan håndhæves med NOT NULL på de tre kolonner i rækken.
   - C: Kan håndhæves af FOREIGN KEY til de tre tabeller users, trips og products.

5. En betaling skal referere til en eksisterende billet.
   - A: Kan håndhæves med NOT NULL på kolonnen ticket_id.
   - C: Kan håndhæves med FOREIGN KEY til tickets-tabellen.

6. Valideringer skal referere til en eksisterende billet.
   - A: Kan håndhæves med NOT NULL på kolonnen ticket_id.
   - C: Kan håndhæves med FOREIGN KEY til tickets-tabellen.

7. Billetkoder skal være unikke.
   - B: Kan håndhæves med UNIQUE på kolonnen code.

8. En billets gyldighed kan ikke udløbe før den starter.
   - A: Kan håndhæves med CHECK (start < end).

9. Status værdier skal komme fra et fast sæt.
   - A: Kan håndhæves med en ENUM eller CHECK (status IN (...)).

10. En ekstern betalingsreference må ikke ved en fejl repræsentere den samme betaling mere end én gang.
   - B: Kan håndhæves med partial UNIQUE INDEX på external_payment_reference WHERE status IN ('Captured', 'Refunded').

11. En brugers email skal være unik.
   - B: Kan håndhæves med UNIQUE på email kolonnen.

12. En brugt billet kan ikke valideres igen.
   - B: Kan håndhæves med UNIQUE på ticket_id kolonnen i validations
   - D: Kan håndhæves af Trigger/applikation

13. Produkter, billetter og betalinger skal have en gyldig valuta.
   - A: Kan håndhæves med NOT NULL og CHECK (regex) på de tre tabeller.

# Valg

1. Status på payments

Valget blev at ændre status til Failed, Captured og Refunded for at reflekterer implementeringen i labbet.

Alternativet var at inkludere Pending, Authorized og Cancelled, som blev fravalgt for at holde det simpelt.

Trade-off på det valg, er at vi ikke kan repræsentere en betaling der er undervejs.

2. Data til rapport

Valget blev at bruge en function(operator_id, date), som har de fordele at logikken er samlet ét sted,
data er aldrig stale, og vi får data per operator.

Alternativet var MV som sænker read cost, på trods af at data kan blive forældet hvis det ikke bliver refreshed.
Det er dog accepteret at rapport data må være eventually consistent. Det blev fravalgt, fordi read cost ikke er et problem på
nuværende tidspunkt.

Trade-off på det valg er en højere read cost end MV.
