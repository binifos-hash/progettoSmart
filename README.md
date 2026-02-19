# SmartWork (React + ASP.NET Core)

Progetto minimale per inviare richieste di "smart" (smart working) su un giorno specifico.

Struttura:
- `server/SmartWorkApi` - backend ASP.NET Core minimal API (in-memory)
- `client` - frontend React (Vite)

Avvio:

1. Backend (richiede .NET 7):

```bash
cd server/SmartWorkApi
dotnet run
```

L'API sarà disponibile su `http://localhost:5000`.

2. Frontend (Node + npm):

```bash
cd client
npm install
npm run dev
```

Apri `http://localhost:5173` per usare l'interfaccia.
