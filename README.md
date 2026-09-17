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

## Diretrizes de design

Interface (console de gestão e QR code de pareamento) não deve ter "cara de IA" — sem os clichês visuais genéricos de interface gerada por IA. Buscar direção visual própria e intencional antes de qualquer implementação de UI.

## Stack recomendada

.NET/C#. Justificativa: acesso nativo à API de spooler de impressão do Windows (`System.Printing` e P/Invoke em `winspool.drv`), maturidade para Windows Service e empacotamento de instalador (MSI assinado), e caminho direto para uma futura interface gráfica em WPF ou .NET MAUI reaproveitando o mesmo código do serviço.

## Fases do projeto

1. Agente Host + Agente Cliente com comunicação básica e descoberta automática (substituição funcional do SMB).
2. Pareamento via TOTP e emissão de token de longa duração.
3. Log de auditoria, alertas (offline/papel/tinta-toner) e estatísticas de uso, com envio por e-mail e persistência em banco.
4. Console de gestão central (monitoramento, revogação remota, atualização automática dos agentes).
5. Backlog futuro: failover entre impressoras e impressão segura (pull printing), avaliados conforme demanda de clientes corporativos.
