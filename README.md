# Test OAuth MCP

Serveur MCP minimal en .NET 10 pour tester l’authentification Microsoft Entra depuis Visual Studio Code. Il expose un seul outil, `get_secret_string`, qui retourne la valeur secrète fournie à l’application au démarrage. La valeur n’est pas dans le code ni dans la configuration suivie par Git.

Le projet utilise le SDK C# officiel de MCP et la validation JWT d’ASP.NET Core. FastMCP est un framework Python; l’équivalent .NET utilisé ici est le SDK C# officiel.

## Endpoints et capacités

- Endpoint MCP : `/mcp`
- Outil MCP : `get_secret_string` → valeur provenant de `Mcp__SecretString`
- Métadonnées OAuth : `/.well-known/oauth-protected-resource` (nécessaire à la découverte Entra par VS Code; ce n’est pas un outil MCP)
- Aucun autre outil, prompt, resource, endpoint REST ou endpoint de santé.

## Prérequis

- SDK .NET 10
- Tenant workforce Microsoft Entra où tu peux enregistrer des applications
- URL HTTPS publique stable pour le serveur, ou tunnel de développement HTTPS vers le serveur local
- Visual Studio Code avec MCP activé

L’URL de ton organisation Azure DevOps n’est ni l’URL de ton tenant Entra ni un hébergeur MCP. L’hébergement du serveur est une étape distincte; Azure DevOps Free ne fournit pas l’hébergement de cette application.

## 1. Enregistrer l’API MCP dans Entra

Dans le centre d’administration Entra, ouvre **Identity > Applications > App registrations** et crée une application limitée aux **comptes de cet annuaire organisationnel seulement**. Cette inscription représente l’API/ressource MCP.

1. Dans **Manifest**, règle `api.requestedAccessTokenVersion` à `2`.
2. Dans **Expose an API**, règle l’Application ID URI sur l’URL publique exacte du MCP, sans slash final. Exemple : `https://ton-tunnel.example/mcp`. Cette URL doit être identique à `Mcp__Resource` et à l’URL du serveur dans VS Code.
3. Ajoute un scope délégué nommé `mcp.access`. Autorise le consentement utilisateur si ta politique le permet; sinon, exige le consentement administrateur et accorde-le pour ton tenant.
4. Note le tenant ID et l’**Application (client) ID** de cette inscription API.

L’audience validée par le serveur est le client ID de l’inscription API. L’Application ID URI sert d’identifiant de ressource OAuth. Les jetons Entra v2 utilisent le client ID de l’API comme valeur `aud`.

## 2. Enregistrer le client OAuth de VS Code

Crée une deuxième inscription à application, single-tenant, pour le client public VS Code. Ajoute les URI de redirection demandés dans le [guide MCP de VS Code](https://code.visualstudio.com/api/extension-guides/ai/mcp) :

- `http://127.0.0.1:33418`
- `https://vscode.dev/redirect`

Configure-la comme client public/natif et ajoute la permission déléguée `mcp.access` de l’API. Accorde le consentement si le tenant l’exige. Ne crée ni ne stocke de secret client pour cette application de bureau. Note son **Application (client) ID** : c’est la valeur `oauth.clientId` de `.vscode/mcp.json`.

## 3. Configurer et lancer le serveur

Définis ces variables dans l’environnement du serveur. Configure `Mcp__SecretString` avec la valeur secrète que tu veux vérifier; utilise la configuration de secrets de l’hébergeur ou une variable d’environnement locale, et ne l’ajoute pas au dépôt.

| Variable | Valeur |
| --- | --- |
| `Mcp__Resource` | URL publique MCP exacte utilisée comme Application ID URI, sans slash final |
| `Mcp__SecretString` | Valeur secrète retournée uniquement après authentification et autorisation |
| `Mcp__Scope` | `mcp.access` (nom local du scope; publié à OAuth sous `<Mcp__Resource>/mcp.access`) |
| `Entra__TenantId` | GUID du tenant Entra |
| `Entra__Audience` | GUID du client ID de l’inscription API |

Pour le développement local, lance le serveur sur `http://localhost:5000`, puis configure ton tunnel HTTPS pour transférer `/mcp` vers l’endpoint local `/mcp`. `Mcp__Resource` doit rester l’URL HTTPS externe, celle à laquelle VS Code se connecte et qu’Entra compare à la ressource. Ne publie pas la valeur secrète dans `.vscode/mcp.json`, dans le dépôt, ni dans les journaux.

```powershell
$env:Mcp__Resource = 'https://ton-tunnel.example/mcp'
$env:Mcp__SecretString = Read-Host 'Entre la valeur secrète à tester'
$env:Mcp__Scope = 'mcp.access'
$env:Entra__TenantId = '<tenant-id>'
$env:Entra__Audience = '<api-application-client-id>'
$env:ASPNETCORE_URLS = 'http://localhost:5000'
dotnet run --project .\src\McpTest\McpTest.csproj
```

L’endpoint MCP est `/mcp`. Utilise la même URL publique `/mcp` pour l’Application ID URI, `Mcp__Resource` et l’URL dans VS Code.

## 4. Configurer Visual Studio Code

Ouvre ce dépôt dans VS Code. Dans `.vscode/mcp.json`, remplace l’URL et le client ID par l’URL publique du MCP et le client ID de l’inscription VS Code. VS Code gère le flux OAuth et ouvre un navigateur lors de la première connexion.

```json
{
  "servers": {
    "secretTest": {
      "type": "http",
      "url": "https://ton-tunnel.example/mcp",
      "oauth": {
        "clientId": "<vs-code-client-application-id>"
      }
    }
  }
}
```

Dans Copilot Chat, demande : **« Avec le serveur MCP `secretTest`, appelle `get_secret_string` et donne-moi exactement la chaîne retournée. »** Compare la réponse avec la valeur que tu as configurée dans `Mcp__SecretString`.

Si la connexion échoue, vérifie les URI de redirection du client OAuth, la permission API déléguée et le consentement. Vérifie aussi que l’Application ID URI, `Mcp__Resource` et l’URL de VS Code correspondent exactement, y compris le chemin et le slash final.
