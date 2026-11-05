Serves the client applications of an ASP.NET host (its TypeScript configurations that are client applications) under
the host's origin: `app.MapClients()` serves their built files ("dist") in production, and in Development starts their
dev server ("ng serve") and forwards to it, unless their "dist" is used (`UseDist`, `Clients:UseDist`). The list comes
from the generated `ClientDirectory` (CK.TypeScript).
