**DECISÕES TRAVADAS:**

1. `testingclaude` confirmado vazio em auditoria ao vivo (git clone \+ fetch em 2026-07-20) — nenhum código migrado ainda.  
2. Estratégia de migração: cópia integral de `src/`, `sql/`, `tests/`, `docs/` para o destino; `game/` é reconstruído do zero, não copiado.  
3. Sequência de commits fixada: **C1** (fundação headless) → **C2** (casca Godot 3D) → **C3** (cenas de modo) → **C4** (README \+ arquivamento da origem).  
4. `LifeSimScene.cs` (stub de 13 linhas) descartado — nada a preservar.  
5. `MatchScene.cs` (ticker: placar/relógio/feed/box score) portado como lógica; só a raiz de nó muda de `Node2D` para `Node3D`.  
6. `GameBootstrap.cs`, `TimeManagerNode.cs`, `EventBusNode.cs`, `SaveServiceNode.cs`, `GameModeManager.cs` portam quase intactos.  
7. `docs/ARCHITECTURE.md` permanece canônico, sem edição.  
8. `SoccerDreamGame` vira arquivo morto (read-only) pós-migração — sem sincronização bidirecional, *one way*.

**EM ABERTO / DISCUTIR:**

1. Renderer 3D em `project.godot` (`forward_plus` vs. `mobile`) — não decidido, depende de plataforma-alvo.  
2. Nome do produto no destino: renomear `.sln`/namespace para "Terra Paralela" já no C1 (recomendei, você não confirmou) vs. manter nome genérico do repo.  
3. Divergências código × design adiadas para prompts próprios, citadas mas não resolvidas aqui: Resolvers Tier 2/3 (Elo atual vs. duplo Poisson do design), Tier 1.5 inexistente, Tactics→Behavior sem código, `FormMood` no schema seguindo modelo antigo (-5/+5, reset sazonal).  
4. **GDD §1** marcado como obsoleto sob D1/Opção B — sinalizado, mas a reescrita não foi feita nesta conversa.  
5. Nenhum commit real foi feito em `testingclaude` — o plano existe, a execução não começou.  
6. **SUSPEITA a verificar no ledger:** a trilha primária que você declarou para esta triagem é "2D primário / dual-track", mas o conteúdo integral desta conversa trata de migrar para 3D como repositório canônico daqui em diante. Verificar se "dual-track" já contempla isso ou se a trilha primária mudou desde a última atualização do ledger.

