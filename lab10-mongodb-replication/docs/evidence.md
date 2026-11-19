# Lecture 10 evidence

**Note: Formater output med pipe | python -m json.tool**
**Note: export MSYS_NO_PATHCONV=1**

## Experiments

Use a new ID for each independent trial. Keep the same ID when inspecting or
retrying the same logical request. Save the full JSON beside these notes.

| Member state                                          | Action | Operation ID | Read/write settings                                       | Prediction          | Actual result / saved output                              |
|-------------------------------------------------------|--------|--------------|-----------------------------------------------------------|---------------------|-----------------------------------------------------------|
| 3 running, primary: mongo1                            | write  | BASE-MAJ     | `w: "majority"`, `wtimeout: 2000`, readPreference primary | 2 acks required     | Acknowledged, 105 ms                                      |
| same                                                  | write  | BASE-ONE     | `w: 1`, `wtimeout: 2000`, readPreference primary          | 1 ack required      | Acknowledged, 90 ms                                       |
| same                                                  | read   | BASE-MAJ     | readPreference primary, readConcern local                 | exists              | Present, 84 ms                                            |
| same                                                  | read   | BASE-MAJ     | readPreference secondary, readConcern local               | exists              | Present, 80 ms                                            |
| mongo1 (primary) stopped                              | read   | BASE-MAJ     | readPreference secondary, readConcern local               | exists              | Present, 180 ms                                           |
| same                                                  | write  | FAILOVER-01  | `w: "majority"`, `wtimeout: 2000`, readPreference primary | 2 acks required     | Acknowledged, 186 ms; ny primary mongo2 (term 2)          |
| mongo1 stopped, primary: mongo2                       | write  | FAILOVER-02  | `w: "majority"`, `wtimeout: 2000`, readPreference primary | 2 acks required     | Acknowledged, 177 ms                                      |
| same                                                  | write  | ACK-ALL      | `w: 3`, `wtimeout: 2000`, readPreference primary          | fejl, kun 2 kan ACK | UnknownWriteOutcome (WriteConcernFailed, `n: 1`), 2234 ms |
| same                                                  | read   | ACK-ALL      | readPreference primary, readConcern local                 | exists              | Present, 201 ms                                           |
| mongo1 + mongo3 stopped, mongo2 SECONDARY, no primary | read   | BASE-MAJ     | readPreference primary, readConcern local                 | fejl, ingen primary | UnknownReadFailure (`EAI_AGAIN mongo1`), 3164 ms          |
| same                                                  | read   | BASE-MAJ     | readPreference secondary, readConcern local               | exists              | Present, 179 ms                                           |
| same                                                  | write  | NO-MAJ-ONE   | `w: 1`, `wtimeout: 2000`, readPreference primary          | fejl, ingen primary | UnknownWriteOutcome (`EAI_AGAIN mongo1`), 3159 ms         |
| same                                                  | write  | NO-MAJ-MAJ   | `w: "majority"`, `wtimeout: 2000`, readPreference primary | fejl, ingen primary | UnknownWriteOutcome (`EAI_AGAIN mongo1`), 3159 ms         |
| 3 running, primary: mongo2                            | write  | RECOVERED-01 | `w: "majority"`, `wtimeout: 2000`, readPreference primary | 2 acks required     | Acknowledged, 315 ms                                      |

## Read-only verification

| Operation ID | Earlier write outcome                                     | Verification result   | What this establishes / leaves uncertain                                                               |
|--------------|-----------------------------------------------------------|-----------------------|--------------------------------------------------------------------------------------------------------|
| BASE-MAJ     | Acknowledged (`w: majority`, 3 kørende)                   | PresentMatches        | Gemt og bekræftet af et flertal, overlevede failover                                                   |
| BASE-ONE     | Acknowledged (`w: 1`), skrevet 15/11                      | AbsentAtVeerification | Ikke et replikeringsresultat: volumes blev oprettet på ny 16/11, så de gamle data findes ikke          |
| FAILOVER-01  | Acknowledged (`w: majority`, ny primary, term 2)          | PresentMatches        | Ny primary (mongo2) tog imod writes efter failover                                                     |
| FAILOVER-02  | Acknowledged (`w: majority`, ny primary, term 2)          | PresentMatches        | Samme som FAILOVER-01, to af tre er nok til majority                                                   |
| ACK-ALL      | UnknownWriteOutcome (WriteConcernFailed, `n: 1`, `ok: 1`) | PresentMatches        | Fejlen var fordi der ikke blev bekrædtet fra 3, dokumentet var gemt og nåede majority (2 af 3)         |
| NO-MAJ-ONE   | UnknownWriteOutcome (ingen primary, `response: null`)     | AbsentAtVerification  | Write nåede aldrig en primary, ændret `w` hjælper ikke uden primary                                    |
| NO-MAJ-MAJ   | UnknownWriteOutcome (ingen primary)                       | AbsentAtVerification  | Samme som NO-MAJ-ONE                                                                                   |
| RECOVERED-01 | Acknowledged (`w: majority`, efter genopretning)          | PresentMatches        | Sættet er raskt igen                                                                                   |
| NEVER-SENT   | (ikke sendt, kontrol)                                     | AbsentAtVerification  | Viser hvordan "aldrig skrevet" ser ud. Fravær nu beviser ikke, at et ukendt udfald aldrig kan dukke op |

## Application policy

| Operation       | Freshness/consistency needed        | During an outage                                                                                     | What the user sees                                   | After recovery                                                                                                            | Reason / evidence                                                                                                                                                                                              |
|-----------------|-------------------------------------|------------------------------------------------------------------------------------------------------|------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Journey search  | Må gerne være lidt stale            | Læs fra secondary, hvis alle er nede fejlbesked. Burde dog have en regel for hvor stale data må være | Data kan være n minutter forældet / Ikke tilgængelig | Opdater cache / data                                                                                                      | Journey seach er read, og der er det fint at læse fra en replica, ligesom read BASE-MAJ secondary gav Present, selvom der ikke var en primary, det betyder data kan være stale, ift. aflysninger og ændringer. |
| Ticket purchase | Fresh/Strong                        | Afvis betaling                                                                                       | Prøv igen senere                                     | Forsøg at verify det samme ID, før der prøves igen. ACK-ALL beviste, at selvom klienten får en fejl, blev det stadig gemt | Hvis primary er nede kan vi ikke skrive til databasen og en kø ville kræve en ekstra dependecy, der også kan fejle.                                                                                            |
| Reporting       | Fresh/Strong, readConcern: majority | Afvis read, og vent til primary er oppe                                                              | Det er ikke muligt at få rapport ligenu              | Ikke noget, vi har afvist adgang under outage                                                                             | Raportering må gerne være forsinket, men ikke forkert. Derfor er det bedre at afvise, og prøve igen senere.                                                                                                    |

## AI second opinion

- Prompt and model:
    - Prompt: Her er min compareChange og ACK-ALL's write- og read-output. Foreslå ét manglende check eller udfordrer
      min
      fortolkning.
    - Model: Claude Opus 5.5
- Suggested claim or experiment:
    - Du skriver, at ACK-ALL "var opfyldt med majority 2" på skrivetidspunktet. Men write-outputtet viser kun n: 1   
      fra primary. Din read ACK-ALL primary brugte local read concern, og din verify kørte først, da alle tre var oppe
      igen. Ingen af dem beviser, at dokumentet var majority-committed, mens mongo1 var nede.
- My prediction:
    - At det giver samme resultat, fordi 2 ud af 3 er majority
- Actual output:
    - En secondary stoppet (2 af 3 kørende, primary tilgængelig). Fuld JSON under "# 5: AI Eksperiment".
    - `write ACK-ALL-02 3` (14:10:10): UnknownWriteOutcome, WriteConcernFailed (wtimeout), `n: 1`, 2262 ms.
    - `verify ACK-ALL-02` (14:11:32), majority read concern, medlemmet stadig nede: PresentMatches, 229 ms.
    - `verify ACK-ALL-02` (14:14:09), majority read concern: PresentMatches, 364 ms.
    - Samme mønster som ACK-ALL: fejl på write concern, men dokumentet er gemt. Nu er det også vist, at det var
      majority committed, mens kun 2 af 3 kørte.
- Decision and remaining uncertainty:
  - Jeg er enig i AI'ens observation. Det var også min egen fortolkning, men jeg blev gjort opmærksom på
    at min test reelt ikke viste det jeg sagde, fordi alle instanser var oppe i min test. 
  - Testen er kun kørt med ét ID og en stoppet instans.

# 1: Read the cancellation from another member

- primary: mongo1
- secondaries: mongo2, mongo3

## 1.1: Predict numbers of acks each write requires

- write BASE-MAJ majority: 2

````json
{
  "action": "write",
  "operationId": "BASE-MAJ",
  "startedAt": "2026-11-15T20:01:57.288Z",
  "readPreference": "primary",
  "readConcern": null,
  "writeConcern": {
    "w": "majority",
    "wtimeout": 2000
  },
  "retryWrites": false,
  "retryReads": false,
  "result": {
    "n": 1,
    "electionId": {
      "$oid": "7fffffff0000000000000001"
    },
    "opTime": {
      "ts": {
        "$timestamp": {
          "t": 1794772917,
          "i": 2
        }
      },
      "t": 1
    },
    "ok": 1,
    "$clusterTime": {
      "clusterTime": {
        "$timestamp": {
          "t": 1794772917,
          "i": 2
        }
      },
      "signature": {
        "hash": {
          "$binary": {
            "base64": "AAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            "subType": "00"
          }
        },
        "keyId": 0
      }
    },
    "operationTime": {
      "$timestamp": {
        "t": 1794772917,
        "i": 2
      }
    }
  },
  "outcome": "Acknowledged",
  "finishedAt": "2026-11-15T20:01:57.393Z",
  "durationMs": 105
}
````

- write BASE-ONE 1: 1

````json
{
  "action": "write",
  "operationId": "BASE-ONE",
  "startedAt": "2026-11-15T20:04:34.794Z",
  "readPreference": "primary",
  "readConcern": null,
  "writeConcern": {
    "w": 1,
    "wtimeout": 2000
  },
  "retryWrites": false,
  "retryReads": false,
  "result": {
    "n": 1,
    "electionId": {
      "$oid": "7fffffff0000000000000001"
    },
    "opTime": {
      "ts": {
        "$timestamp": {
          "t": 1794773074,
          "i": 1
        }
      },
      "t": 1
    },
    "ok": 1,
    "$clusterTime": {
      "clusterTime": {
        "$timestamp": {
          "t": 1794773074,
          "i": 1
        }
      },
      "signature": {
        "hash": {
          "$binary": {
            "base64": "AAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            "subType": "00"
          }
        },
        "keyId": 0
      }
    },
    "operationTime": {
      "$timestamp": {
        "t": 1794773074,
        "i": 1
      }
    }
  },
  "outcome": "Acknowledged",
  "finishedAt": "2026-11-15T20:04:34.884Z",
  "durationMs": 90
}
````

- Begge writes returnerer acks. Vi kan kun se at primary giver acks, men vi ved at den første bruger majority, så derfra
  ved vi at
  mindst en anden har acks også. I One ved vi kun at primary har.

- read BASE-MAJ primary

````json
{
  "action": "read",
  "operationId": "BASE-MAJ",
  "startedAt": "2026-11-15T20:12:27.351Z",
  "readPreference": "primary",
  "readConcern": "local",
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "actual": {
    "_id": "BASE-MAJ",
    "tripId": "T102",
    "status": "Cancelled",
    "sourceVersion": 2
  },
  "outcome": "Present",
  "finishedAt": "2026-11-15T20:12:27.435Z",
  "durationMs": 84
}
````

- read BASE-MAJ secondary

````json
{
  "action": "read",
  "operationId": "BASE-MAJ",
  "startedAt": "2026-11-15T20:13:56.942Z",
  "readPreference": "secondary",
  "readConcern": "local",
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "actual": {
    "_id": "BASE-MAJ",
    "tripId": "T102",
    "status": "Cancelled",
    "sourceVersion": 2
  },
  "outcome": "Present",
  "finishedAt": "2026-11-15T20:13:57.022Z",
  "durationMs": 80
}
````

- Vi kan konstatere, at både primary og mindst én secondary har modtaget BASE-MAJ write.
  vi kan stadig have en secondary som er stale.

- En secondary kan godt være stale en anden gang, fordi replikeringen er asynkron, og primary venter ikke på
  secondaries,
  medmindre write concern kræver det.

# Stop the primary

## Find the primary:

- Primary er mongo1

````json
{
  "action": "status",
  "operationId": null,
  "startedAt": "2026-11-16T12:08:07.966Z",
  "readPreference": "nearest",
  "readConcern": null,
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "contactedMember": "mongo1",
  "outcome": "StatusRead",
  "members": [
    {
      "name": "mongo1:27017",
      "state": "PRIMARY",
      "health": 1,
      "self": true,
      "optimeDate": {
        "$date": "2026-11-16T12:08:01Z"
      }
    },
    {
      "name": "mongo2:27017",
      "state": "SECONDARY",
      "health": 1,
      "self": false,
      "optimeDate": {
        "$date": "2026-11-16T12:08:01Z"
      }
    },
    {
      "name": "mongo3:27017",
      "state": "SECONDARY",
      "health": 1,
      "self": false,
      "optimeDate": {
        "$date": "2026-11-16T12:08:01Z"
      }
    }
  ],
  "finishedAt": "2026-11-16T12:08:08.151Z",
  "durationMs": 185
}
````

- write BASE-MAJ majority

- read BASE-MAJ secondary

````json
{
  "action": "read",
  "operationId": "BASE-MAJ",
  "startedAt": "2026-11-16T12:09:07.094Z",
  "readPreference": "secondary",
  "readConcern": "local",
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "actual": {
    "_id": "BASE-MAJ",
    "tripId": "T102",
    "status": "Cancelled",
    "sourceVersion": 2
  },
  "outcome": "Present",
  "finishedAt": "2026-11-16T12:09:07.274Z",
  "durationMs": 180
}
````

- FAILOVER-01 majority

````json
{
  "action": "write",
  "operationId": "FAILOVER-01",
  "startedAt": "2026-11-16T12:09:13.227Z",
  "readPreference": "primary",
  "readConcern": null,
  "writeConcern": {
    "w": "majority",
    "wtimeout": 2000
  },
  "retryWrites": false,
  "retryReads": false,
  "result": {
    "n": 1,
    "electionId": {
      "$oid": "7fffffff0000000000000002"
    },
    "opTime": {
      "ts": {
        "$timestamp": {
          "t": 1794830953,
          "i": 1
        }
      },
      "t": 2
    },
    "ok": 1,
    "$clusterTime": {
      "clusterTime": {
        "$timestamp": {
          "t": 1794830953,
          "i": 1
        }
      },
      "signature": {
        "hash": {
          "$binary": {
            "base64": "AAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            "subType": "00"
          }
        },
        "keyId": 0
      }
    },
    "operationTime": {
      "$timestamp": {
        "t": 1794830953,
        "i": 1
      }
    }
  },
  "outcome": "Acknowledged",
  "finishedAt": "2026-11-16T12:09:13.413Z",
  "durationMs": 186
}
````

- run.sh status mongo2

````json
{
  "action": "status",
  "operationId": null,
  "startedAt": "2026-11-16T12:10:18.586Z",
  "readPreference": "nearest",
  "readConcern": null,
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "contactedMember": "mongo2",
  "outcome": "StatusRead",
  "members": [
    {
      "name": "mongo1:27017",
      "state": "(not reachable/healthy)",
      "health": 0,
      "self": false,
      "optimeDate": {
        "$date": "1970-01-01T00:00:00Z"
      }
    },
    {
      "name": "mongo2:27017",
      "state": "PRIMARY",
      "health": 1,
      "self": true,
      "optimeDate": {
        "$date": "2026-11-16T12:10:11Z"
      }
    },
    {
      "name": "mongo3:27017",
      "state": "SECONDARY",
      "health": 1,
      "self": false,
      "optimeDate": {
        "$date": "2026-11-16T12:10:11Z"
      }
    }
  ],
  "finishedAt": "2026-11-16T12:10:18.758Z",
  "durationMs": 172
}
````

- write FAILOVER-02 majority

````json
{
  "action": "write",
  "operationId": "FAILOVER-02",
  "startedAt": "2026-11-16T12:10:48.912Z",
  "readPreference": "primary",
  "readConcern": null,
  "writeConcern": {
    "w": "majority",
    "wtimeout": 2000
  },
  "retryWrites": false,
  "retryReads": false,
  "result": {
    "n": 1,
    "electionId": {
      "$oid": "7fffffff0000000000000002"
    },
    "opTime": {
      "ts": {
        "$timestamp": {
          "t": 1794831049,
          "i": 1
        }
      },
      "t": 2
    },
    "ok": 1,
    "$clusterTime": {
      "clusterTime": {
        "$timestamp": {
          "t": 1794831049,
          "i": 1
        }
      },
      "signature": {
        "hash": {
          "$binary": {
            "base64": "AAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            "subType": "00"
          }
        },
        "keyId": 0
      }
    },
    "operationTime": {
      "$timestamp": {
        "t": 1794831049,
        "i": 1
      }
    }
  },
  "outcome": "Acknowledged",
  "finishedAt": "2026-11-16T12:10:49.089Z",
  "durationMs": 177
}
````

- run.sh write ACK-ALL 3
- Forventer en fejl, fordi der ikke er 3 instanser til at ACK

````json
{
  "action": "write",
  "operationId": "ACK-ALL",
  "startedAt": "2026-11-16T12:16:03.464Z",
  "readPreference": "primary",
  "readConcern": null,
  "writeConcern": {
    "w": 3,
    "wtimeout": 2000
  },
  "retryWrites": false,
  "retryReads": false,
  "outcome": "UnknownWriteOutcome",
  "error": {
    "code": 64,
    "codeName": "WriteConcernFailed",
    "message": "waiting for replication timed out",
    "errorLabels": [],
    "response": {
      "code": 64,
      "codeName": "WriteConcernFailed",
      "errmsg": "waiting for replication timed out",
      "errInfo": {
        "wtimeout": true,
        "writeConcern": {
          "w": 3,
          "wtimeout": 2000,
          "provenance": "clientSupplied"
        }
      },
      "n": 1,
      "electionId": {
        "$oid": "7fffffff0000000000000002"
      },
      "opTime": {
        "ts": {
          "$timestamp": {
            "t": 1794831363,
            "i": 1
          }
        },
        "t": 2
      },
      "writeConcernError": {
        "code": 64,
        "codeName": "WriteConcernFailed",
        "errmsg": "waiting for replication timed out",
        "errInfo": {
          "wtimeout": true,
          "writeConcern": {
            "w": 3,
            "wtimeout": 2000,
            "provenance": "clientSupplied"
          }
        }
      },
      "ok": 1,
      "$clusterTime": {
        "clusterTime": {
          "$timestamp": {
            "t": 1794831363,
            "i": 1
          }
        },
        "signature": {
          "hash": {
            "$binary": {
              "base64": "AAAAAAAAAAAAAAAAAAAAAAAAAAA=",
              "subType": "00"
            }
          },
          "keyId": 0
        }
      },
      "operationTime": {
        "$timestamp": {
          "t": 1794831363,
          "i": 1
        }
      }
    }
  },
  "finishedAt": "2026-11-16T12:16:05.698Z",
  "durationMs": 2234
}
````

- run.sh read ACK-ALL primary
- Forventer at kunne læse. Fejlen fra før betyder ikke at det ikke blev skrevet, men bare at det ikke kunne bekræftes af
  3 instanser, altså at writeConcern ikke kunne opfyldes
- Timeout kan altså ikke bruges til at konstatere, at dataen ikke blev gemt.

````json
{
  "action": "read",
  "operationId": "ACK-ALL",
  "startedAt": "2026-11-16T12:17:37.973Z",
  "readPreference": "primary",
  "readConcern": "local",
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "actual": {
    "_id": "ACK-ALL",
    "tripId": "T102",
    "status": "Cancelled",
    "sourceVersion": 2
  },
  "outcome": "Present",
  "finishedAt": "2026-11-16T12:17:38.174Z",
  "durationMs": 201
}
````

# 3: Lose the majority

- docker compose start mongo1
- bash run.sh status

````json
{
  "action": "status",
  "operationId": null,
  "startedAt": "2026-11-16T12:19:02.890Z",
  "readPreference": "nearest",
  "readConcern": null,
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "contactedMember": "mongo1",
  "outcome": "StatusRead",
  "members": [
    {
      "name": "mongo1:27017",
      "state": "SECONDARY",
      "health": 1,
      "self": true,
      "optimeDate": {
        "$date": "2026-11-16T12:19:01Z"
      }
    },
    {
      "name": "mongo2:27017",
      "state": "PRIMARY",
      "health": 1,
      "self": false,
      "optimeDate": {
        "$date": "2026-11-16T12:19:01Z"
      }
    },
    {
      "name": "mongo3:27017",
      "state": "SECONDARY",
      "health": 1,
      "self": false,
      "optimeDate": {
        "$date": "2026-11-16T12:19:01Z"
      }
    }
  ],
  "finishedAt": "2026-11-16T12:19:03.105Z",
  "durationMs": 215
}
````

- Nu er mongo2 primary
- docker compose stop mongo1 mongo3
- run.sh status

````json
{
  "action": "status",
  "operationId": null,
  "startedAt": "2026-11-16T12:20:39.035Z",
  "readPreference": "nearest",
  "readConcern": null,
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "contactedMember": "mongo2",
  "outcome": "StatusRead",
  "members": [
    {
      "name": "mongo1:27017",
      "state": "(not reachable/healthy)",
      "health": 0,
      "self": false,
      "optimeDate": {
        "$date": "1970-01-01T00:00:00Z"
      }
    },
    {
      "name": "mongo2:27017",
      "state": "SECONDARY",
      "health": 1,
      "self": true,
      "optimeDate": {
        "$date": "2026-11-16T12:20:11Z"
      }
    },
    {
      "name": "mongo3:27017",
      "state": "(not reachable/healthy)",
      "health": 0,
      "self": false,
      "optimeDate": {
        "$date": "1970-01-01T00:00:00Z"
      }
    }
  ],
  "finishedAt": "2026-11-16T12:20:39.252Z",
  "durationMs": 217
}
````

- Nu er de to andre nede, og mongo2 er blevet secondary
- Det ses altså at vi ikke længere har en primary. Mongo2 kan ikke se de andre instanser, og vælger derfor at træde ned
  fra at være primary
  fordi den ikke længere kan snakke med majority

- bash run.sh read BASE-MAJ primary
- Forventer at read fejler, fordi den forsøger at læse fra pirmary, som ikke findes pt.

````json
{
  "action": "read",
  "operationId": "BASE-MAJ",
  "startedAt": "2026-11-16T12:31:37.306Z",
  "readPreference": "primary",
  "readConcern": "local",
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "outcome": "UnknownReadFailure",
  "error": {
    "code": null,
    "codeName": null,
    "message": "getaddrinfo EAI_AGAIN mongo1",
    "errorLabels": [],
    "response": null
  },
  "finishedAt": "2026-11-16T12:31:40.470Z",
  "durationMs": 3164
}
````

- bash run.sh read BASE-MAJ secondary
- forventer present, da mongo2 er secondary

````json
{
  "action": "read",
  "operationId": "BASE-MAJ",
  "startedAt": "2026-11-16T12:33:46.119Z",
  "readPreference": "secondary",
  "readConcern": "local",
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "actual": {
    "_id": "BASE-MAJ",
    "tripId": "T102",
    "status": "Cancelled",
    "sourceVersion": 2
  },
  "outcome": "Present",
  "finishedAt": "2026-11-16T12:33:46.298Z",
  "durationMs": 179
}
````

- bash run.sh write NO-MAJ-ONE 1
- Forventer at den fejler, selvom den kun har ACK 1, skal write gå gennem primary, som stadig ikke findes.

````json
{
  "action": "write",
  "operationId": "NO-MAJ-ONE",
  "startedAt": "2026-11-16T12:34:16.232Z",
  "readPreference": "primary",
  "readConcern": null,
  "writeConcern": {
    "w": 1,
    "wtimeout": 2000
  },
  "retryWrites": false,
  "retryReads": false,
  "outcome": "UnknownWriteOutcome",
  "error": {
    "code": null,
    "codeName": null,
    "message": "getaddrinfo EAI_AGAIN mongo1",
    "errorLabels": [],
    "response": null
  },
  "finishedAt": "2026-11-16T12:34:19.391Z",
  "durationMs": 3159
}
````

- bash run.sh write NO-MAJ-MAJ majority
- Forventer stadig fejl, fordi vi forsøger at write, og vi har kun en secondary.

````json
{
  "action": "write",
  "operationId": "NO-MAJ-MAJ",
  "startedAt": "2026-11-16T12:35:31.032Z",
  "readPreference": "primary",
  "readConcern": null,
  "writeConcern": {
    "w": "majority",
    "wtimeout": 2000
  },
  "retryWrites": false,
  "retryReads": false,
  "outcome": "UnknownWriteOutcome",
  "error": {
    "code": null,
    "codeName": null,
    "message": "getaddrinfo EAI_AGAIN mongo1",
    "errorLabels": [],
    "response": null
  },
  "finishedAt": "2026-11-16T12:35:34.191Z",
  "durationMs": 3159
}
````

- Ændringen af readPreference fra primary til secondary gør at vi kan læse, fordi vi kun har secondary
- Ændring af w for writes ændrer ikke noget, fordi vi KUN kan skrive til en primary, som der ikke er.

- bash run.sh up
- bash run.up status

````json
{
  "action": "status",
  "operationId": null,
  "startedAt": "2026-11-16T12:38:40.354Z",
  "readPreference": "nearest",
  "readConcern": null,
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "contactedMember": "mongo1",
  "outcome": "StatusRead",
  "members": [
    {
      "name": "mongo1:27017",
      "state": "SECONDARY",
      "health": 1,
      "self": true,
      "optimeDate": {
        "$date": "2026-11-16T12:38:31Z"
      }
    },
    {
      "name": "mongo2:27017",
      "state": "PRIMARY",
      "health": 1,
      "self": false,
      "optimeDate": {
        "$date": "2026-11-16T12:38:31Z"
      }
    },
    {
      "name": "mongo3:27017",
      "state": "SECONDARY",
      "health": 1,
      "self": false,
      "optimeDate": {
        "$date": "2026-11-16T12:38:31Z"
      }
    }
  ],
  "finishedAt": "2026-11-16T12:38:40.578Z",
  "durationMs": 224
}
````

# 4: Check what survived

- bash run.sh check

````json
{
  "test": "no document",
  "outcome": "Pass"
}
{
  "test": "matching document",
  "outcome": "Fail",
  "message": "Expected PresentMatches, got PresentConflicts"
}
{
  "test": "different ID",
  "outcome": "Pass"
}
{
  "test": "different trip",
  "outcome": "Pass"
}
{
  "test": "different status",
  "outcome": "Pass"
}
{
  "test": "different version",
  "outcome": "Pass"
}
{
  "test": "missing version",
  "outcome": "Pass"
}
{
  "passed": 6,
  "failed": 1
}
````

- Andet forsøg

````json
{
  "test": "no document",
  "outcome": "Pass"
}
{
  "test": "matching document",
  "outcome": "Pass"
}
{
  "test": "different ID",
  "outcome": "Pass"
}
{
  "test": "different trip",
  "outcome": "Pass"
}
{
  "test": "different status",
  "outcome": "Pass"
}
{
  "test": "different version",
  "outcome": "Pass"
}
{
  "test": "missing version",
  "outcome": "Pass"
}
{
  "passed": 7,
  "failed": 0
}
````

- bash run.sh write RECOVERED-01 majority

````json
{
  "action": "write",
  "operationId": "RECOVERED-01",
  "startedAt": "2026-11-16T13:16:10.999Z",
  "readPreference": "primary",
  "readConcern": null,
  "writeConcern": {
    "w": "majority",
    "wtimeout": 2000
  },
  "retryWrites": false,
  "retryReads": false,
  "result": {
    "n": 1,
    "electionId": {
      "$oid": "7fffffff0000000000000003"
    },
    "opTime": {
      "ts": {
        "$timestamp": {
          "t": 1794834971,
          "i": 1
        }
      },
      "t": 3
    },
    "ok": 1,
    "$clusterTime": {
      "clusterTime": {
        "$timestamp": {
          "t": 1794834971,
          "i": 1
        }
      },
      "signature": {
        "hash": {
          "$binary": {
            "base64": "AAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            "subType": "00"
          }
        },
        "keyId": 0
      }
    },
    "operationTime": {
      "$timestamp": {
        "t": 1794834971,
        "i": 1
      }
    }
  },
  "outcome": "Acknowledged",
  "finishedAt": "2026-11-16T13:16:11.314Z",
  "durationMs": 315
}
````

- bash run.sh verify BASE-MAJ BASE-ONE FAILOVER-01 FAILOVER-02 \
  ACK-ALL NO-MAJ-ONE NO-MAJ-MAJ RECOVERED-01 NEVER-SENT

````json
{
  "action": "verify",
  "operationId": "BASE-MAJ",
  "startedAt": "2026-11-16T13:16:48.502Z",
  "readPreference": "primary",
  "readConcern": "majority",
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "actual": {
    "_id": "BASE-MAJ",
    "tripId": "T102",
    "status": "Cancelled",
    "sourceVersion": 2
  },
  "outcome": "PresentMatches",
  "finishedAt": "2026-11-16T13:16:48.748Z",
  "durationMs": 246
}
{
  "action": "verify",
  "operationId": "BASE-ONE",
  "startedAt": "2026-11-16T13:16:48.748Z",
  "readPreference": "primary",
  "readConcern": "majority",
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "actual": null,
  "outcome": "AbsentAtVerification",
  "finishedAt": "2026-11-16T13:16:48.790Z",
  "durationMs": 42
}
{
  "action": "verify",
  "operationId": "FAILOVER-01",
  "startedAt": "2026-11-16T13:16:48.790Z",
  "readPreference": "primary",
  "readConcern": "majority",
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "actual": {
    "_id": "FAILOVER-01",
    "tripId": "T102",
    "status": "Cancelled",
    "sourceVersion": 2
  },
  "outcome": "PresentMatches",
  "finishedAt": "2026-11-16T13:16:48.822Z",
  "durationMs": 32
}
{
  "action": "verify",
  "operationId": "FAILOVER-02",
  "startedAt": "2026-11-16T13:16:48.822Z",
  "readPreference": "primary",
  "readConcern": "majority",
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "actual": {
    "_id": "FAILOVER-02",
    "tripId": "T102",
    "status": "Cancelled",
    "sourceVersion": 2
  },
  "outcome": "PresentMatches",
  "finishedAt": "2026-11-16T13:16:48.866Z",
  "durationMs": 44
}
{
  "action": "verify",
  "operationId": "ACK-ALL",
  "startedAt": "2026-11-16T13:16:48.866Z",
  "readPreference": "primary",
  "readConcern": "majority",
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "actual": {
    "_id": "ACK-ALL",
    "tripId": "T102",
    "status": "Cancelled",
    "sourceVersion": 2
  },
  "outcome": "PresentMatches",
  "finishedAt": "2026-11-16T13:16:48.897Z",
  "durationMs": 31
}
{
  "action": "verify",
  "operationId": "NO-MAJ-ONE",
  "startedAt": "2026-11-16T13:16:48.897Z",
  "readPreference": "primary",
  "readConcern": "majority",
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "actual": null,
  "outcome": "AbsentAtVerification",
  "finishedAt": "2026-11-16T13:16:48.931Z",
  "durationMs": 34
}
{
  "action": "verify",
  "operationId": "NO-MAJ-MAJ",
  "startedAt": "2026-11-16T13:16:48.931Z",
  "readPreference": "primary",
  "readConcern": "majority",
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "actual": null,
  "outcome": "AbsentAtVerification",
  "finishedAt": "2026-11-16T13:16:48.961Z",
  "durationMs": 30
}
{
  "action": "verify",
  "operationId": "RECOVERED-01",
  "startedAt": "2026-11-16T13:16:48.961Z",
  "readPreference": "primary",
  "readConcern": "majority",
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "actual": {
    "_id": "RECOVERED-01",
    "tripId": "T102",
    "status": "Cancelled",
    "sourceVersion": 2
  },
  "outcome": "PresentMatches",
  "finishedAt": "2026-11-16T13:16:48.989Z",
  "durationMs": 28
}
{
  "action": "verify",
  "operationId": "NEVER-SENT",
  "startedAt": "2026-11-16T13:16:48.989Z",
  "readPreference": "primary",
  "readConcern": "majority",
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "actual": null,
  "outcome": "AbsentAtVerification",
  "finishedAt": "2026-11-16T13:16:49.026Z",
  "durationMs": 37
}
````

- ACK-ALL gav fejl, fordi kravet om w:3 ikke blev opfyldt, men den blev skrevet, som kan ses af svaret: n:1 ok:1, altså
  1 dokument blev gemt. Fejlen ligger i writeConcernError (wtimeout), ikke i selve indsættelsen. Da write blev lavet,
  kørte 2 af 3 , så majority var allerede opfyldt med 2, derfor kan verify med majority read concern se den.
- NO-MAJ-ONE gav et unknown outcome og blev aldrig gemt, men det ved vi først efter verify, klienten ved ikke at det
  ikke blev gemt. Outputtet viser response: null og getaddrinfo EAI_AGAIN mongo1: klienten fandt aldrig en primary, så
  der er intet n/ok og intet at rulle tilbage. Det samme gælder NO-MAJ-MAJ.
- I NEVER-SENT kan det ses hvordan det ser ud, når noget slet ikke bliver sendt
- BASE-ONE er AbsentAtVerification, fordi den blev skrevet 15/11, og volumes blev oprettet på ny 16/11. Det er altså
  ikke et resultat af replikering.

# 5: AI Eksperiment

1. Stop en secondary
2. write ACK-ALL-02 3
3. verify ACK-ALL-02
4. Genstart og kør run.sh up
5. Hvad viser verify mens medlemmet stadig er nede?

- Write

````json
{
  "action": "write",
  "operationId": "ACK-ALL-02",
  "startedAt": "2026-11-16T14:10:10.157Z",
  "readPreference": "primary",
  "readConcern": null,
  "writeConcern": {
    "w": 3,
    "wtimeout": 2000
  },
  "retryWrites": false,
  "retryReads": false,
  "outcome": "UnknownWriteOutcome",
  "error": {
    "code": 64,
    "codeName": "WriteConcernFailed",
    "message": "waiting for replication timed out",
    "errorLabels": [],
    "response": {
      "code": 64,
      "codeName": "WriteConcernFailed",
      "errmsg": "waiting for replication timed out",
      "errInfo": {
        "wtimeout": true,
        "writeConcern": {
          "w": 3,
          "wtimeout": 2000,
          "provenance": "clientSupplied"
        }
      },
      "n": 1,
      "electionId": {
        "$oid": "7fffffff0000000000000003"
      },
      "opTime": {
        "ts": {
          "$timestamp": {
            "t": 1794838210,
            "i": 1
          }
        },
        "t": 3
      },
      "writeConcernError": {
        "code": 64,
        "codeName": "WriteConcernFailed",
        "errmsg": "waiting for replication timed out",
        "errInfo": {
          "wtimeout": true,
          "writeConcern": {
            "w": 3,
            "wtimeout": 2000,
            "provenance": "clientSupplied"
          }
        }
      },
      "ok": 1,
      "$clusterTime": {
        "clusterTime": {
          "$timestamp": {
            "t": 1794838210,
            "i": 1
          }
        },
        "signature": {
          "hash": {
            "$binary": {
              "base64": "AAAAAAAAAAAAAAAAAAAAAAAAAAA=",
              "subType": "00"
            }
          },
          "keyId": 0
        }
      },
      "operationTime": {
        "$timestamp": {
          "t": 1794838210,
          "i": 1
        }
      }
    }
  },
  "finishedAt": "2026-11-16T14:10:12.419Z",
  "durationMs": 2262
}
````

- Verify

````json
{
  "action": "verify",
  "operationId": "ACK-ALL-02",
  "startedAt": "2026-11-16T14:11:32.847Z",
  "readPreference": "primary",
  "readConcern": "majority",
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "actual": {
    "_id": "ACK-ALL-02",
    "tripId": "T102",
    "status": "Cancelled",
    "sourceVersion": 2
  },
  "outcome": "PresentMatches",
  "finishedAt": "2026-11-16T14:11:33.076Z",
  "durationMs": 229
}
````

- Verify efter genstart:

````json
{
  "action": "verify",
  "operationId": "ACK-ALL-02",
  "startedAt": "2026-11-16T14:14:09.392Z",
  "readPreference": "primary",
  "readConcern": "majority",
  "writeConcern": null,
  "retryWrites": false,
  "retryReads": false,
  "actual": {
    "_id": "ACK-ALL-02",
    "tripId": "T102",
    "status": "Cancelled",
    "sourceVersion": 2
  },
  "outcome": "PresentMatches",
  "finishedAt": "2026-11-16T14:14:09.756Z",
  "durationMs": 364
}
````