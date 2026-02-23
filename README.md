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

## Deploy su Render

Per vedere il frontend online devi pubblicare **due servizi**:

1. **Backend API** (Web Service, Docker)
2. **Frontend React** (Static Site)

### 1) Backend API (Web Service)

- Root Directory: repository root
- Environment: `Docker`
- Dockerfile: `Dockerfile`
- URL attesa (esempio): `https://progettosmart.onrender.com`

Variabili ambiente consigliate:

- `ALLOWED_ORIGINS=https://<tuo-frontend>.onrender.com,https://progettosmart.onrender.com`
- `EMAIL_TRANSPORT=auto` (`auto` | `sendgrid` | `smtp`)
- `EMAIL_FROM=...` (mittente email, es. `noreply@tuodominio.it`)

Per invio email **affidabile su Render** (consigliato):

- `SENDGRID_API_KEY=...`
- (alias supportati: `SENDGRID_KEY`, `SENDGRID_APIKEY`)
- `EMAIL_TRANSPORT=sendgrid` (oppure `auto` per fallback)

Se vuoi usare SMTP classico:

- `SMTP_USERNAME=...`
- `SMTP_PASSWORD=...`
- `SMTP_HOST=smtp.gmail.com`
- `SMTP_PORT=587`
- `SMTP_SECURE=StartTls`
- `SMTP_TIMEOUT_MS=20000`
- `SMTP_GMAIL_465_FALLBACK=true` (default: prova automaticamente porta 465 SSL se 587 fallisce)

Nota: su alcuni hosting la porta SMTP in uscita può essere filtrata; il trasporto `sendgrid` usa HTTPS (porta 443) e di solito evita questi timeout.

### 2) Frontend React (Static Site)

Crea un secondo servizio Render di tipo **Static Site**:

- Root Directory: `client`
- Build Command: `npm ci && npm run build`
- Publish Directory: `dist`

Variabile ambiente obbligatoria:

- `VITE_API_BASE=https://progettosmart.onrender.com`

> Se non imposti `VITE_API_BASE`, il frontend proverà a chiamare `http://localhost:5000` e online non funzionerà.

### Come vedere il frontend

Il frontend sarà disponibile sulla URL della Static Site (es. `https://progettosmart-client.onrender.com`).
La URL `https://progettosmart.onrender.com` resta la API backend.
