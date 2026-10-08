## Print Tools
Ferramenta própria de compartilhamento de impressoras USB locais em rede local, substituindo o compartilhamento nativo do Windows (SMB). Formada por dois agentes: um instalado no PC onde a impressora está fisicamente conectada (Host), e outro nos PCs que precisam imprimir remotamente (Client). Toda a comunicação acontece dentro da rede local, sem qualquer dependência de acesso externo à internet.

## Problema que resolve

O compartilhamento de impressora via SMB no Windows depende de três pontos frágeis simultâneos:

- Autenticação de rede entre as máquinas (erros de permissão constantes).
- Resolução de nome/IP da máquina host (quebra sempre que o IP muda).
- Estabilidade do serviço de spooler do Windows na máquina host.

Essa ferramenta elimina o SMB da equação. O compartilhamento passa a ser feito por um protocolo próprio, com descoberta automática do host na rede (sem IP fixo cadastrado em lugar nenhum) e reconexão automática do lado do cliente.

## Arquitetura

### Agente Host (Windows Service — PC onde a impressora está instalada)

- **Gerenciador de impressoras**: lista as impressoras locais da máquina e permite marcar quais ficam expostas na rede.
- **Servidor de rede**: listener TCP com protocolo próprio. Recebe do cliente o job de impressão já processado e injeta diretamente na fila local via API de spooler do Windows (`StartDocPrinter` / `WritePrinter`).
- **Serviço de descoberta**: anuncia periodicamente na rede local (broadcast UDP ou mDNS) o hostname, IP atual e lista de impressoras disponíveis. Resolve o problema de IP dinâmico na raiz — o cliente nunca depende de IP fixo.
- **Monitoramento de status**: consulta periodicamente cada impressora via `GetPrinter` (offline, sem papel) e, quando possível, via API proprietária do fabricante (nível de tinta/toner — ver seção de riscos técnicos).

### Agente Cliente (roda nas máquinas que imprimem)

- **Listener de loopback**: socket TCP local (`127.0.0.1`), numa porta própria. A impressora é cadastrada no Windows normalmente, com o driver do fabricante, usando uma "Porta TCP/IP Padrão" apontando para esse endereço local. Evita a necessidade de um Port Monitor customizado.
- **Encaminhador**: repassa o que chega nesse socket local para o Agente Host pela rede, com conexão persistente e reconexão automática. Se o host estiver indisponível, o job fica em fila local em disco e é reenviado assim que a conexão volta.
- **Cliente de descoberta**: escuta os anúncios do host na rede e resolve o endereço dinamicamente.

## Segurança — pareamento via TOTP

Autenticação baseada em TOTP (RFC 6238), o mesmo padrão aberto usado pelo Microsoft Authenticator, Google Authenticator e Authy na opção "Adicionar conta > Outra conta". Funciona 100% offline, dentro da LAN, sem depender de Azure AD ou de qualquer serviço externo da Microsoft.

Fluxo:

1. Na primeira execução, o Agente Host gera um segredo TOTP próprio e exibe um QR code. O administrador escaneia uma única vez com o app autenticador de sua preferência.
2. Quando uma máquina nova tenta se conectar a uma impressora pela primeira vez, o Agente Cliente solicita o código atual de 6 dígitos. O administrador confere no app e digita para aprovar.
3. Aprovado o pareamento, o Host emite um token/certificado de longa duração para aquele cliente específico. As impressões seguintes usam esse token via TLS, sem pedir código novamente.
4. O código só volta a ser exigido se o acesso daquela máquina for revogado e ela precisar ser pareada de novo.

## Funcionalidades — v1 (MVP)

- **Log de auditoria**: quem imprimiu, em qual impressora, quantas páginas, quando. Registrado em banco de dados para consulta.
- **Alertas automáticos**, enviados por e-mail e registrados em banco:
  - Impressora offline.
  - Sem papel.
  - Nível de tinta/toner (Epson e Brother — ver ressalva técnica abaixo).
- **Estatísticas de uso**: quantidade de páginas impressas por dia e por mês, por máquina.
- **Console de gestão central**: painel acessível na rede local mostrando todas as impressoras, status em tempo real e quais máquinas estão pareadas em cada uma.
- **Revogação remota de acesso**: o administrador revoga o token de uma máquina específica direto pelo console, sem precisar ir até o PC host.
- **Atualização automática dos agentes**: os dois agentes se atualizam sozinhos a partir de uma nova versão publicada.

## Fora do escopo da v1 (backlog futuro)

- **Failover entre impressoras equivalentes**: redirecionar jobs automaticamente se uma impressora do mesmo modelo cair.
- **Impressão segura (pull printing)**: job retido no Host até autenticação física do usuário na impressora (PIN/crachá). Mais relevante para venda a clientes corporativos do que para o uso interno atual.

## Riscos e observações técnicas

- **Nível de tinta/toner**: impressoras em rede expõem esse dado via SNMP. Como as impressoras deste projeto são USB local, sem interface de rede, esse caminho não existe. A alternativa é ler via API proprietária de status de cada fabricante (EPSON Status Monitor, Brother Status Monitor), que usa comunicação bidirecional própria, não documentada publicamente, e pode variar entre modelos e versões de driver. Página impressa, papel e status online/offline não têm esse risco, pois vêm da API padrão de spooler do Windows.
- **Escopo de rede**: solução pensada exclusivamente para rede local. Nenhum componente deve expor porta para a internet.
- **Escala variável**: o número de impressoras e PCs por loja varia bastante (de uma única impressora até várias máquinas dividindo a mesma impressora). A arquitetura de descoberta automática precisa suportar esse cenário sem configuração manual por máquina.

## Validação manual — Fase 1

Teste ponta a ponta realizado em hardware real: Agente Host numa máquina Windows com uma impressora EPSON L3150 conectada via USB, e Agente Cliente em um notebook separado, ambos na mesma rede local.

- **Descoberta automática**: o Cliente encontrou o Host por sondagem UDP broadcast e resolveu a impressora compartilhada sozinho, sem nenhum IP fixo cadastrado em lugar nenhum.
- **Impressão real**: um documento de teste impresso no Cliente — usando a impressora cadastrada no Windows via "Porta TCP/IP Padrão" apontando para o loopback do Cliente — saiu fisicamente na EPSON L3150 conectada ao Host, percorrendo o caminho completo: driver do Windows → loopback do Cliente → encaminhamento TCP → Host → injeção via `winspool.drv`.
- **Resiliência observada**: quando o processo do Host ou do Cliente foi encerrado e religado, a descoberta automática se recuperou sozinha no ciclo de sondagem seguinte, sem reconfiguração manual.

### Nota de instalação — status SNMP na porta TCP/IP do Cliente

Ao cadastrar a impressora no Windows do Cliente via "Adicionar impressora" → "Porta TCP/IP Padrão" apontando para `127.0.0.1`, o assistente do Windows tenta primeiro consultar o dispositivo via SNMP. Como o Agente Cliente não implementa SNMP, essa consulta falha e aparece o aviso "O dispositivo não foi encontrado na rede" — isso é esperado e não impede a criação da porta; basta manter "Padrão: Generic Network Card" e avançar.

Mais importante: a porta é criada por padrão com **"Habilitar status SNMP"** marcado nas propriedades. Isso pode fazer o Windows reportar falha de impressão mesmo com o Agente Cliente e o Agente Host funcionando corretamente, porque o spooler espera uma resposta SNMP que nunca chega. Para evitar isso:

1. Impressoras e scanners → propriedades da impressora → aba **Portas** → **Configurar Porta**.
2. Protocolo: **Raw**, porta: a mesma porta configurada no `printers.json` do Cliente para essa impressora.
3. Desmarcar **"Habilitar status SNMP"**.

## Como parear uma máquina nova (Fase 2)

A partir da Fase 2, o Host exige pareamento via TOTP antes de aceitar qualquer impressão — veja
"Segurança — pareamento via TOTP" acima. Na prática, em três passos:

1. **No Host**, rode `PrintTool.Host.exe show-totp`. Ele gera (na primeira vez) o segredo TOTP e
   grava um QR code em `security/totp-qrcode.png`, além de imprimir a URI `otpauth://` e o segredo
   em Base32 no console, para quem preferir digitar manualmente. Escaneie com qualquer app
   autenticador (Google/Microsoft Authenticator, Authy — opção "Outra conta").
2. **No Client novo**, rode `PrintTool.Client.exe pair "<Nome exato da impressora>"` (o mesmo nome
   usado em `sharedprinters.json` no Host). O comando procura o Host na rede e pede o código de
   6 dígitos.
3. Digite o código que está no app autenticador nesse instante. Aprovado, o Host emite um token de
   longa duração para esta máquina — as impressões seguintes não pedem código de novo.

Para revogar o acesso de uma máquina (ela precisa ser pareada de novo depois):

```
PrintTool.Host.exe list-clients          # lista as máquinas pareadas e seus ClientId
PrintTool.Host.exe revoke-client <id>    # revoga o acesso de uma delas
```

Os dois comandos (`show-totp`, `pair`, `list-clients`, `revoke-client`) rodam o próprio executável
já instalado como serviço — não sobem um novo serviço, só executam o comando e saem.

### Validação manual — Fase 2

Pareamento e autenticação via TOTP validados em hardware real, nas mesmas duas máquinas da
validação da Fase 1 (Host com a EPSON L3150 via USB, Client num notebook separado), ambas já
rodando como Windows Service:

- `show-totp` no Host gerou o QR code e o segredo corretamente; escaneado num app autenticador.
- `pair` no Client resolveu o Host pela descoberta UDP, pediu o código de 6 dígitos e, aprovado,
  recebeu e gravou o token de longa duração.
- A impressão seguinte autenticou via TLS usando esse token (sem pedir código de novo) e saiu
  fisicamente na EPSON — confirmando o caminho completo: driver → loopback → TLS + token → Host →
  `winspool.drv`.
- **Lacuna encontrada e corrigida durante a validação**: rodando como Windows Service, a aplicação
  não tinha console nem nenhum log persistente — só o registro padrão do Windows sobre o ciclo de
  vida do serviço aparecia no Visualizador de Eventos, nada da aplicação em si. Corrigido
  adicionando o provedor de Event Log (`Microsoft.Extensions.Logging.EventLog`), ativo somente
  quando roda como serviço (fonte `PrintTool.Host` / `PrintTool.Client` no log "Aplicativo"), para
  não depender de reiniciar em modo console toda vez que for preciso diagnosticar algo em produção.

## Apps de administração (Host e Client)

O dia a dia — compartilhar uma impressora nova, parear uma máquina nova, revogar acesso —
não depende mais de abrir terminal nem editar JSON na mão. Dois apps WPF mínimos (abrir, usar,
fechar — sem bandeja do sistema, sem consumir recursos quando fechados) cobrem exatamente os
mesmos comandos de CLI acima, lendo e escrevendo os mesmos arquivos (`sharedprinters.json`,
`printers.json`, pasta `security/`) que o serviço Windows já usa:

- **PrintTool.Host.UI** — três telas: impressoras locais (toggle "compartilhada" por impressora),
  pareamento (QR code + segredo em texto, equivalente a `show-totp`) e máquinas pareadas (status
  ativo/revogado + botão "Revogar", equivalente a `list-clients`/`revoke-client`).
- **PrintTool.Client.UI** — três telas: impressoras vistas na rede (descoberta automática),
  conectar (escolhe a impressora, digita o código de 6 dígitos num campo estilo "passcode",
  equivalente a `pair`) e impressoras configuradas (status pareado/pendente + repetir pareamento
  ou remover).

Os comandos de CLI continuam existindo como caminho alternativo/headless — nenhum foi removido.

O serviço Windows passou a observar `sharedprinters.json` (Host) e `printers.json` (Client) e
recarregar sozinho quando esses arquivos mudam (`FileSystemWatcher`), em até poucos segundos —
sem isso, os apps só editariam arquivo e o problema original (precisar reiniciar o serviço pelo
terminal) continuaria existindo disfarçado.

`PrintTool.Host.UI` e `PrintTool.Client.UI` publicam **direto na mesma pasta** do agente
correspondente (`PrintTool.Host`/`PrintTool.Client`), não numa pasta própria — é assim que os
dois lêem/escrevem exatamente os mesmos arquivos (`security/`, `sharedprinters.json`,
`printers.json`) sem precisar copiar nada manualmente. Como consequência, **pare o serviço E
feche a janela do app de administração, se estiver aberta,** antes de publicar — os três
(serviço, `Host.UI`/`Client.UI` publicado antes, e o novo publish) disputam os mesmos `.dll`
na mesma pasta:

```
Stop-Service PrintTool.Host
dotnet publish src\PrintTool.Host.UI -c Release
Start-Service PrintTool.Host
```

```
Stop-Service PrintTool.Client
dotnet publish src\PrintTool.Client.UI -c Release
Start-Service PrintTool.Client
```

Os scripts `install-host.ps1`/`install-client.ps1` criam automaticamente um atalho no Menu
Iniciar ("PrintTool Host"/"PrintTool Client") apontando pro executável publicado, se ele existir.

### Validação manual — apps de administração

Pareamento e impressão validados de ponta a ponta pela interface gráfica, nas mesmas duas
máquinas das validações anteriores (Host "TI2" com a EPSON L3150, Client num notebook separado):
abrir o `PrintTool.Host.UI`, ligar o toggle de compartilhamento, abrir o `PrintTool.Client.UI`,
achar a impressora na aba Rede, conectar, digitar o código de 6 dígitos do autenticador na tela
de Parear e ver o status virar "Pareado" — sem editar nenhum JSON nem rodar comando de CLI em
nenhum dos dois lados. Cadastrada a porta no Windows (`127.0.0.1:<porta local mostrada no app>`,
SNMP desmarcado — mesma nota de instalação da Fase 1), a impressão saiu fisicamente na EPSON.

Como era a primeira vez rodando WPF de verdade (só dava pra escrever o código às cegas neste
ambiente, sem conseguir compilar), apareceram vários problemas só visíveis em uso real,
corrigidos nesta ordem:

- **`T?` num genérico sem restrição não vira `Nullable<T>`** — `RelayCommand<Guid>` não compilava
  porque `Action<T?>` com `T` livre apaga pra `Action<T>` mesmo quando `T` é um tipo valor; só
  vira `Nullable<T>` de verdade com `where T : struct`. Resolvido convertendo o parâmetro do
  comando com `is T value ? value : default!` em vez de depender da anotação `?`.
- **Propriedade de controle customizado precisa de `set` público pro XAML compilar** — o
  compilador de markup exige um acessador `set` acessível pra permitir `Code="{Binding ...}"`
  como atributo, mesmo sendo um binding; `private set` dá erro em tempo de compilação (MC3080),
  não é contornado em runtime como seria de esperar.
- **Exceção não tratada num `async void` mata o app WPF em silêncio** — sem um handler de
  `DispatcherUnhandledException` (e sem captura dentro do próprio `AsyncRelayCommand`), qualquer
  falha de rede durante o pareamento fechava a janela sem nenhuma mensagem. Os dois apps agora
  mostram a exceção numa caixa de mensagem em vez de simplesmente morrer — foi assim que os
  próximos dois problemas desta lista ficaram visíveis pra corrigir.
- **`StaticResource` dentro de um `Style` inline embutido num `DataTemplate` não resolve contra
  `Application.Resources` de forma confiável** — carrega de forma adiada
  (`FrameworkTemplate.LoadTemplateXaml`), e nesse caminho o WPF só enxerga os recursos da própria
  `Window`, não os da aplicação. `DynamicResource` não serve de alternativa porque `Style.BasedOn`
  não é uma `DependencyProperty`. Corrigido mesclando o design system compartilhado direto em
  `Window.Resources`, além do `Application.Resources` que já tinha.
- **Os dois apps de administração publicavam cada um na sua própria pasta** —
  `PrintTool.Host.UI`/`PrintTool.Client.UI` acabavam com seu próprio `security/totp-secret.json`,
  diferente do que o serviço real usa pra validar — o QR code mostrado nunca batia com o código
  aceito. Corrigido fazendo os dois publicarem direto na pasta do agente correspondente
  (`PublishDir` nos `.csproj`), e documentando que o app aberto (não só o serviço) trava os `.dll`
  compartilhados durante um novo publish.
- **`DiscoveryRefreshService` ainda injetava `ClientPrinterMappingConfig` por tipo** — sobrou do
  refactor de hot-reload do `LoopbackServer` (que passou a receber o caminho do arquivo, não o
  objeto já carregado); sem o registro no DI, o `PrintTool.Client` inteiro falhava silenciosamente
  ao subir (`Status: Stopped` sem nenhum erro no Event Log, porque o processo morria antes de
  logar qualquer coisa). Só apareceu rodando o executável direto no console, fora do modo serviço.
  Corrigido recarregando o mapeamento do disco a cada ciclo de sondagem, mesmo padrão já usado
  pelo `LoopbackServer`.
- **Revogar pelo Host.UI não tinha efeito imediato** — testado manualmente: revogar a máquina e
  tentar imprimir de novo continuava funcionando. Duas causas combinadas: (1) `PrintServer`
  guardava a autenticação como um bool validado uma única vez por conexão TCP, e como o Client
  mantém conexão persistente (vários jobs, uma conexão), revogar só valia pra conexões novas; (2)
  `ClientTokenStore` carregava `tokens.json` uma única vez na inicialização do serviço e nunca
  recarregava — diferente do `SharedPrintersConfig`/`ClientPrinterMappingConfig`, que já tinham
  esse hot-reload desde o começo da Fase 2.1, um padrão que passou batido aqui porque
  `ClientTokenStore` tem o mesmo formato (escrito pelo Host.UI, lido pelo serviço já rodando).
  Corrigido revalidando o token a cada job (não só uma vez por conexão) e dando ao
  `ClientTokenStore` o mesmo `FileSystemWatcher` dos outros dois. Revalidado manualmente: revogar
  agora derruba a próxima impressão na hora, sem precisar reiniciar nada.

## Alertas de conectividade (Fase 3, parcial)

O Host monitora sozinho se as máquinas pareadas (e não revogadas) continuam com uma conexão
autenticada ativa, e manda um e-mail de alerta **só quando esse estado muda** — não um e-mail a
cada checagem. A primeira vez que uma máquina é vista nunca gera alerta (linha de base
silenciosa, para não disparar "desconectado" pra todo mundo assim que o serviço sobe); alertas
acontecem só numa transição real: conectado → desconectado ("Máquina desconectada") ou o
contrário ("Conexão restaurada"). Máquinas revogadas são ignoradas — não ter conexão é esperado
nesse caso.

Fora do horário comercial configurado, a checagem inteira é pulada — uma máquina desligada de
noite ou no fim de semana não é um problema, então não gera alerta nenhum.

Configuração em `security/alerting.json` (criado automaticamente, com horário seg-sex 08:00-18:00
e e-mail desativado, na primeira vez que o serviço sobe — edite o arquivo direto, ele nunca entra
no Git):

```json
{
  "Smtp": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "SenderEmail": "alertas@gmail.com",
    "SenderAppPassword": "xxxxxxxxxxxxxxxx",
    "RecipientEmail": "admin@empresa.com"
  },
  "CheckIntervalMinutes": 5,
  "BusinessHours": {
    "Monday": { "Start": "08:00:00", "End": "18:00:00" },
    "Saturday": null,
    "Sunday": null
  }
}
```

- **`SenderAppPassword`** precisa ser uma "Senha de app" do Gmail (Conta Google → Segurança →
  Verificação em duas etapas → Senhas de app), não a senha normal da conta — o Gmail não aceita
  SMTP com a senha normal. Enviar e-mail direto de um servidor local (sem usar um relay como o
  Gmail) não é uma alternativa confiável: a maioria dos provedores de internet bloqueia a porta
  25 de saída, e mesmo sem bloqueio, provedores de e-mail rejeitam remetentes sem reputação.
- Um dia sem horário definido (`null`, como sábado/domingo no padrão) fica fechado o dia inteiro.
- Enquanto `Smtp` não estiver preenchido (`SenderEmail`/`SenderAppPassword`/`RecipientEmail`
  vazios), o monitoramento roda normalmente mas só loga um aviso em vez de tentar enviar e-mail —
  não trava nada.
- Envio via MailKit (o `SmtpClient` embutido do .NET está obsoleto).

Implementado e com os testes automatizados passando — **ainda não validado em hardware real**
(próximo passo).

## Diretrizes de design

Interface (console de gestão e QR code de pareamento) não deve ter "cara de IA" — sem os clichês visuais genéricos de interface gerada por IA. Buscar direção visual própria e intencional antes de qualquer implementação de UI.

Os apps de administração (`PrintTool.Host.UI`, `PrintTool.Client.UI`) seguem essa diretriz com
uma linha minimalista inspirada em Apple/Samsung: paleta neutra com um único tom de destaque,
tipografia Segoe UI Variable, cantos arredondados e bastante espaço em branco, backdrop Mica
nativo do Windows 11 (com degradação silenciosa em versões sem suporte), modo claro/escuro
seguindo o tema do Windows, e campo de código estilo "passcode" da Apple para o pareamento.

## Stack recomendada

.NET/C#. Justificativa: acesso nativo à API de spooler de impressão do Windows (`System.Printing` e P/Invoke em `winspool.drv`), maturidade para Windows Service e empacotamento de instalador (MSI assinado), e caminho direto para uma futura interface gráfica em WPF ou .NET MAUI reaproveitando o mesmo código do serviço.

## Fases do projeto

1. Agente Host + Agente Cliente com comunicação básica e descoberta automática (substituição funcional do SMB).
2. Pareamento via TOTP e emissão de token de longa duração.
2.1. Apps de administração em WPF (Host e Client) substituindo o fluxo manual de CLI/JSON do dia a dia — ver "Apps de administração" acima.
3. Log de auditoria, alertas (offline/papel/tinta-toner) e estatísticas de uso, com envio por e-mail e persistência em banco — parte retomada (alertas de conectividade, ver "Alertas de conectividade" acima); log de auditoria e estatísticas de uso continuam em espera.
4. Console de gestão central (monitoramento, revogação remota, atualização automática dos agentes).
5. Backlog futuro: failover entre impressoras e impressão segura (pull printing), avaliados conforme demanda de clientes corporativos.
