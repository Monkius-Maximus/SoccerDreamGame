# Terra Paralela — Registro de Decisões: Arquétipos, Coesão e Vida de Carreira

> **Natureza deste documento.** Não é companion doc novo — é um **registro de decisões** que fecha as pendências levantadas na triagem da sessão de arquétipos/relações e reconcilia os artefatos dela com o ledger canônico. Escrito assim deliberadamente para respeitar **D-08** (moratória de design: nenhum companion doc novo até o steering passar no harness), seguindo o mesmo padrão do *Registro de Decisões: Camada de Autoria de Mundo*.
>
> **Cross-ref:** *Prestígio de Competições* · *Geração de Identidade de Clube v2* · *Tactics → Behavior* · GDD §3 (Event System) · GDD §4 (Personality / Form-Mood) · GDD §5 (LOD) · `docs/ARCHITECTURE.md`.
> Convenção: prosa PT-BR, identificadores em inglês. Determinismo obrigatório (SplitMix64, streams isolados).
>
> **Escopo coberto:** sistema de arquétipos de jogador, tiering de traits, reconciliação do prestígio de competição, coesão/química de elenco, simulação de vida no modo carreira, modelo `Agent`. **Fora:** match engine, Tier 1.5, arbitragem, camada de autoria de mundo (já fechada em registro próprio).
>
> **Trilha:** **Core** (`src/SoccerSim.Core/`) em tudo que este registro decide. Nenhuma decisão aqui depende de projeção visual. A única superfície 2D futura é a **apresentação** da camada de vida, fora do escopo deste registro.

---

## 1. Por que este registro existe

A sessão produziu três artefatos — o survey de arquétipos (com duas propostas de design embutidas), o `Squad_Cohesion_Chemistry_Design_Doc` e o `Career_Life_Relationships_Design_Doc`. A triagem os classificou como 🟡 por quatro motivos: o split não foi executado, as propostas não foram ratificadas, decisões de design ficaram abertas, e a proposta de prestígio **colide com um arquivo de pesquisa já canônico**.

Este registro resolve os quatro. A ordem não é arbitrária: a colisão de prestígio vem primeiro porque é a única que envolve uma **contradição factual** entre artefatos, e resolvê-la muda o que sobra dos outros.

---

## 2. Reconciliação do prestígio — o artefato novo estava **errado**, e a pesquisa canônica está certa

Este é o item mais importante do registro.

**A colisão.** O arquivo canônico *Sistema de Prestígio de Competições* (pesquisa, Eixos A–D) e o `DOCUMENT 3` do survey de arquétipos cobrem o mesmo domínio. Não são camadas complementares inofensivas: eles **discordam na mecânica**.

**A contradição.** O `DOCUMENT 3` propôs um `growthModifier` em que competição de prestígio mais alto rende **bônus de crescimento** — uma rampa monotônica. A pesquisa canônica refuta exatamente isso no veredito do Eixo C: o driver é **minutos competitivos em nível adequado**, não dificuldade máxima; e a engenharia reversa do FM (FM-Arena, thread 13975) mede que reputação de liga isolada **não tem efeito** sobre crescimento de atributo.

**Resolução: a pesquisa canônica prevalece.** O `DOCUMENT 3` não é uma segunda fonte de verdade; é uma proposta que a evidência já derrubou.

**D-30 — O `DOCUMENT 3` do survey de arquétipos está formalmente superado.** A pesquisa *Sistema de Prestígio de Competições* permanece a fonte única de verdade para prestígio e desenvolvimento. O `DOCUMENT 3` é marcado como **superado**, não como pendente — mesma disciplina aplicada ao P5 em D-10. O que sobrevive dele é o recorte de "competição internacional" (§2.2), reescrito abaixo para caber na curva de fit.

**D-31 — O modelo de desenvolvimento é curva de fit, não multiplicador de tier.** Fica travada a forma da pesquisa: `gain = f(minutos, fit(nível do jogador, difficultyTarget), idade, posição, performance)`. O `leagueTier` **define o alvo de dificuldade** contra o qual o fit é medido; ele não multiplica ganho. Empréstimo para baixo pode **acelerar** desenvolvimento, e isso é comportamento correto, não bug.

**D-32 — Competição internacional entra como `difficultyTarget`, não como bônus.** O pedido original de tratar Champions/Libertadores como caso especial se resolve **sem caminho novo**: a competição continental tem `difficultyTarget` próprio, mais alto que o do campeonato doméstico do mesmo clube. Ele entra na *mesma* curva de fit. Consequência deliberada e desejada: para um jovem em formação, a competição continental pode ser dificuldade **acima** da janela ótima e render **menos** que o campeonato nacional — que é precisamente o que a literatura sustenta e o que um multiplicador monotônico erraria.

`difficultyTarget` é propriedade da **competição**, computada uma vez por temporada. Isso a torna disponível de forma idêntica em Tier 1, 2 e 3 — um caminho, sem versão especial por tier.

**D-33 — Ranking de seleções alimenta o adversário, nunca o jogador.** O ranking estilo FIFA/Elo por nação entra **exclusivamente** para derivar o `difficultyTarget` de partidas de seleção (força do adversário). **Nunca** modula o desenvolvimento de um jogador em função da nacionalidade dele.

A justificativa é a queixa que originou a sessão: um jogador indiano ser sistematicamente rebaixado *por ser indiano* é o viés que queremos eliminar, não reproduzir com outra roupa. Um jogador de nação fraca que atua numa liga forte é medido pela liga; se atua pela seleção contra um adversário fraco, é a **partida** que é fácil, não ele. A nacionalidade não é input do desenvolvimento em nenhum ponto.

**D-34 — Força de nação permanece input de autoria de mundo, não de simulação de carreira.** Onde a força da nação legitimamente importa (demografia da geração, banda da liga daquele país) ela já entra pelas bandas **B1–B6** do doc de Prestígio. Não há segundo canal.

---

## 3. Sistema de arquétipos — o que fica travado

O `DOCUMENT 2` do survey sobrevive, mas **reancorado** no schema real do projeto. Como escrito, ele assumia um pool de ~35 atributos ao estilo EA e um vocabulário de arquétipos importado de FM/PES — ambos incompatíveis com o que já está travado.

**D-35 — Modelo de três camadas travado, sobre o schema existente.** `AttributeId` (os **12 atributos**, enum único de D-27) → `PlayerArchetype` → `Trait` (o conjunto de ~41 já existente). Nenhum atributo novo, nenhum schema paralelo. Reafirma D-26: goleiro não ganha campo próprio; a semântica de GK sai da tabela de pesos por posição.

**D-36 — O vocabulário de arquétipos é o do ledger, não o do survey.** Os nomes citados no `DOCUMENT 2` (`GoalPoacher`, `Mezzala`, `InsideForward`…) eram **ilustração de survey**, não proposta de adoção. O vocabulário canônico é o que já está travado — `RegistaCM` (D-23), `CreatorAdvanced` (D-24), dois arquétipos de GK e não três (D-25). Adotar a lista do survey criaria a árvore paralela que D-12 existe para impedir.

> 🟡 **Reconciliação exigida no ledger:** a lista completa e atual de `PlayerArchetype` não é visível nesta sessão. Antes de qualquer handoff, conferir a lista canônica e verificar se há lacuna de cobertura posicional. Não invento entradas novas aqui.

**D-37 — Arquétipo é template de geração, não lógica de runtime.** Um `PlayerArchetype` carrega: (a) parâmetros de distribuição por `AttributeId`, (b) `CompatiblePositions`, (c) pesos de afinidade de trait. É consumido pelo `PlayerGenerator` na **ferramenta de autoria offline**. Coerente com D-11 (âncora carrega só arquétipo, banda de qualidade e demografia) e com D-15 (nada de terceiros no build do jogo). Nenhuma regra de arquétipo existe em runtime; o jogo lê dado inerte.

**D-38 — OVR e cor de carta são derivados, nunca persistidos.** OVR é média ponderada por posição, computada em tempo de leitura via `EffectiveAttributes.Read()` — ponto de acesso único, sem exceção. A **cor de carta é função do OVR**, portanto também derivada e também não persistida. Armazenar qualquer um dos dois recria exatamente o débito de calibração que o projeto já paga.

**D-39 — Sem inflação de reputação no OVR.** O mecanismo documentado do EA — um bônus aditivo de International Reputation somado por cima da média ponderada, sem tocar em nenhum atributo — **não é replicado**. Reputação é campo próprio, visível, com consumidores próprios (interesse de clubes, salário, mídia). O número da carta reflege habilidade, e só. Esta é a decisão que responde diretamente à queixa de origem da sessão.

**D-40 — Traits ganham três tiers, com vocabulário que não colide.** `TraitTier`: `Emerging` (Latente) · `Established` (Consolidado) · `Signature` (Assinatura).

Deliberadamente **não** usa Bronze/Prata/Ouro: essas palavras já pertencem ao tier de carta, e reusá-las para trait recriaria a colisão de nomenclatura que D-23 acabou de resolver. Um vocabulário, um referente.

**D-41 — O tiering de trait é o instrumento de correção do débito de limiar.** As dívidas conhecidas (*Maestro* em ~0,2% da população, *Bombardeiro* em ~12,8% dos ST) se resolvem estruturalmente: o limiar atual vira o piso de **`Signature`**, e `Emerging`/`Established` recebem limiares mais baixos. Um trait deixa de ser binário raro e vira gradiente.

Os limiares numéricos **não entram aqui** — "research before thresholds". O que entra é o alvo de calibração: distribuição-alvo por tier declarada antes de qualquer número ser fixado, e verificada contra a população gerada.

**D-42 — Contagem de traits é limitada por `qualityBand`, não por cor de carta.** Cor de carta deriva do OVR (D-38), que deriva dos atributos, que dependem da geração — limitar trait por cor de carta criaria dependência circular. A banda de qualidade é **input** da geração (D-11), então é o ponto de corte correto. O erro estava no `DOCUMENT 2`; fica corrigido.

**D-43 — Bandas de cor de carta são dependência declarada, não número inventado.** As faixas do EA (Bronze ≤64 / Prata 65–74 / Ouro 75+) pressupõem uma distribuição de OVR que não é a nossa — o projeto tem **débito de teto de OVR** conhecido (máx. ~79 no tier 1.00 contra alvo 86–88). Fixar bandas antes de fechar o teto seria calibrar contra uma escala que vai mudar. As bandas são fixadas **depois** que o teto de OVR estiver corrigido, contra a distribuição real gerada.

---

## 4. Coesão e química — pendências fechadas

Referente ao `Squad_Cohesion_Chemistry_Design_Doc`.

**D-44 — Camada A é não-negativa; Camada B pode ser negativa.** A Química de Identidade (nacionalidade/clube/liga/role fit) **nunca penaliza**: incompatibilidade de identidade rende zero, jogador atua nos atributos-base — mesma postura do EA pós-FUT 23, e a correta, porque punir um elenco cosmopolita seria modelar preconceito, não futebol. Os Grupos Sociais (Camada B) **podem** ser negativos: vestiário rachado prejudicando o time é o fenômeno real que o sistema existe para capturar.

**D-45 — Camada B tem teto maior que Camada A.** As duas escrevem num único `cohesionModifier`, e o teto de contribuição da Camada A é estritamente menor que o da B. Motivo: a realidade do vestiário deve poder dominar a afinidade de passaporte, nunca o contrário. Os pesos exatos são **calibração de game feel** contra o harness (mesma disciplina de D-04), não achado de pesquisa.

**D-46 — Coesão é limitada pela faixa de Form/Mood.** O `cohesionModifier` vive dentro da faixa de modificador já estabelecida no GDD §4 (−5…+5). Não existe escala paralela.

**D-47 — Coesão nunca toca física.** Reafirmação de D-06 por analogia: o `cohesionModifier` modula **pesos da camada de decisão** e Form/Mood; nunca rolagem/quique, nunca atributo bruto. Entra na simulação exclusivamente via `EffectiveAttributes.Read()` — não há segundo ponto de aplicação.

**D-48 — Em seleção nacional, o eixo de nacionalidade é desligado.** Numa seleção a nacionalidade é constante e o eixo degenera para máximo trivial, virando ruído. Nesse contexto a química vem de clube/liga compartilhados e de role fit. Não é fallback: é a mesma função lendo um contexto declarado.

**D-49 — Staff fica fora do MVP de coesão.** O modelo cobre staff por construção, mas a implementação valida primeiro o grafo de jogadores. Abstração diferida aplicada corretamente — generalizar no segundo caso de uso real, não no primeiro imaginado (mesmo raciocínio de D-25 e D-9.4).

**D-50 — O jogador nunca lê o `CohesionContext` verdadeiro.** O que a UI mostra é um `ScoutedCohesionView`, função da competência do olheiro/auxiliar. O estado verdadeiro é interno. Isso dá valor concreto a contratar bom olheiro e é fiel ao precedente (a tela de Dynamics do FM é opinião da comissão, sujeita a erro), sem custo de sistema novo.

**D-51 — Agrupamento social é função pura, sem PRNG.** `SocialGroup` sai de similaridade determinística sobre Personality Vector + idioma + faixa etária + tempo de casa + profissionalismo. Zero sorteio. Consequência: mundos já gerados não são invalidados quando o agrupamento for ajustado, e o replay bit a bit é preservado.

---

## 5. Vida de carreira — pendências fechadas

Referente ao `Career_Life_Relationships_Design_Doc`.

**D-52 — Teto de conteúdo: maturidade por consequência, não por conteúdo explícito.** Fica travado o eixo: as consequências podem ser duras — relação termina, patrocínio cai, reputação afunda, minutagem some, escândalo persiste por temporadas. O que **não** entra: conteúdo sexual explícito e loops de jogo de azar como mecânica de recompensa.

Três motivos, nesta ordem: (1) o valor de design que a sessão pediu é *consequência*, e consequência não depende de explicitude — o custo social de uma escolha é o que morde; (2) o concorrente mais próximo (*Football Life Simulator*) já ocupou a rota adulta, e segui-la troca diferenciação por sobreposição; (3) preserva alcance de classificação etária sem amputar o design. "Nada wholesome" continua valendo: o jogo não segura a mão do jogador. Ele só não vira outro gênero.

**D-53 — Autonomia NPC↔NPC existe só no modo `Profundo`.** No `Padrao`, NPCs reagem ao jogador; não agem entre si. Motivo duplo: custo de simulação e legibilidade — evento entre dois NPCs que o jogador não observou é consequência sem causa visível, que é exatamente a "opacidade causal" que o projeto identificou como origem da sensação de script.

**D-54 — O modo de profundidade é travado por save.** Escolhido na criação da carreira, imutável depois. Permitir troca no meio seria dar ao jogador uma saída de emergência das consequências — que anula a premissa central do modo `Profundo`. Um caminho por save.

**D-55 — Menu social do MVP: quatro ações, uma por eixo de relação.** `Confront` (Rivalidade) · `Support` (Amizade) · `RequestTransfer` (RespeitoProfissional, via agente) · `MediaStatement` (reputação/mídia). Quatro ações provam o ciclo completo `ação → RelationshipState → evento → consequência`. Expandir só depois que o ciclo estiver validado — a terceira ação confirma o padrão, não a primeira.

---

## 6. O modelo `Agent` — promovido a decisão de primeira classe

A triagem sinalizou corretamente: o `Agent` é a espinha dos dois docs e não tinha artefato. Fica registrado aqui.

**D-56 — `Agent` é o modelo único de pessoa.** Composição travada:

| Componente | Origem | Observação |
| :--- | :--- | :--- |
| `roleArchetype` | Mesmo padrão de `PlayerArchetype` | `Coach.*`, `Scout.*`, `Agent.*`, `Director.*`, `Journalist.*`, `Referee.*` |
| `PersonalityVector` | Já existente (GDD §4) | Estático, atribuído na geração |
| `Trait[]` | Conjunto já existente | Com `TraitTier` (D-40) |
| `CompetenceAttributes` | Escala 1–99, por função | Treinador ≠ olheiro ≠ médico |
| `RelationshipState[]` | **Única peça nova** | Multi-eixo, com decaimento e histórico |

Jogador é um `Agent` com `isPlayerCharacter`. **Não existe** "sistema de treinador" + "sistema de agente" + "sistema de jornalista" em paralelo — seria a duplicação que o projeto proíbe.

**D-57 — `RefereeProfile` não é absorvido pelo `Agent` na v1.** O árbitro já tem contrato próprio (função pura lendo contexto de falta emitido pelo resolvedor). Reescrevê-lo como `Agent` agora seria reforma oportunista sobre um sistema que já funciona. A unificação só se justifica se e quando o árbitro precisar de relação persistente com jogadores — segundo caso de uso real, não o primeiro imaginado.

**D-58 — Sequência de construção da camada de pessoas, atrás de D-07/D-14.** Ordem travada: **staff de desenvolvimento** (treinador/olheiro/médico) → **agente** → **imprensa** → **esfera pessoal/romance**. Começa pelo mais mecânico e menos narrativo, que valida o `Agent` com risco mínimo e pluga direto no ciclo de vida do jogador.

Isto **não** reordena a fila. A prioridade vigente segue `System.Random` → SplitMix64, depois física headless, steering, primeiro sprite (D-07). Esta camada pertence à janela de sprints posterior, junto com a autoria de mundo (D-14).

---

## 7. Determinismo

**D-59 — Três streams novos, nomeados antes da implementação.** `StreamName` recebe: `cohesion_events`, `career_life_events`, `player_generation_archetype`. Isolados entre si e dos streams de partida. Regra herdada da §8 do doc de Identidade de Clube: **stream novo entra no fim da ordem de consumo**, para não deslocar sequências de mundos já gerados.

Reafirmação: o agrupamento social não consome PRNG (D-51). Só eventos consomem.

---

## 8. Higiene documental

**D-60 — O survey de arquétipos entra no ledger fatiado, com status por parte.**

| Parte | Status |
| :--- | :--- |
| `DOCUMENT 1` — survey de precedentes | 🟢 Entra como **pesquisa**, fonte válida |
| `DOCUMENT 2` — proposta de arquétipos | 🟡 Entra **reancorado** por D-35…D-43; o que contradiz este registro está superado |
| `DOCUMENT 3` — proposta de prestígio | 🔴 **Superado** por D-30 |

**D-61 — A tese do "piso legal" permanece hipótese não verificada.** A explicação de que EA/Konami evitariam notas baixas por risco jurídico de difamação **não tem confirmação de fonte primária** e não pode virar premissa de design. O que está documentado é o mecanismo de inflação aditiva por International Reputation — e é só sobre esse fato que D-39 se apoia. Registrado para que a hipótese não vire fato por repetição.

**D-62 — O split em companion docs fica adiado até D-08 ser levantada.** Quando o steering passar no harness, o material deste registro e dos dois docs de sessão vira: *Sistema de Arquétipos e Traits*, *Coesão de Elenco*, *Vida e Relações de Carreira*. Até lá, este registro é a fonte de verdade dessas decisões. Criar os três agora violaria a moratória sem ganho.

---

## 9. O que permanece deliberadamente em aberto

Registrado para não virar decisão por omissão:

1. **Lista canônica de `PlayerArchetype`** — reconciliação exigida no ledger (D-36); pode haver lacuna de cobertura posicional.
2. **Limiares numéricos de `TraitTier`** — dependem da distribuição-alvo e da população gerada (D-41).
3. **Faixas de cor de carta** — bloqueadas pelo débito de teto de OVR (D-43).
4. **Pesos de Camada A vs. Camada B** — calibração de game feel contra o harness (D-45).
5. **Magnitude do `difficultyTarget` por competição e forma exata da curva de fit** — já listadas como decisão de design (não de pesquisa) no doc de Prestígio; permanecem lá, não migram para cá.
6. **Consumidores concretos de `CompetenceAttributes`** por função — dependem do doc de ciclo de vida fechar.

---

## Sumário das decisões travadas neste registro

| Id | Decisão |
| :--- | :--- |
| D-30 | `DOCUMENT 3` (prestígio) superado; pesquisa canônica prevalece |
| D-31 | Desenvolvimento é curva de fit; `leagueTier` define alvo, não multiplica |
| D-32 | Competição internacional entra como `difficultyTarget`, não como bônus |
| D-33 | Ranking de seleções alimenta dificuldade do adversário, nunca a nacionalidade do jogador |
| D-34 | Força de nação é input de autoria de mundo, via bandas B1–B6 |
| D-35 | Três camadas: `AttributeId` (12) → `PlayerArchetype` → `Trait` |
| D-36 | Vocabulário de arquétipos é o do ledger; lista do survey era ilustração |
| D-37 | Arquétipo é template de geração offline, não lógica de runtime |
| D-38 | OVR e cor de carta são derivados, nunca persistidos |
| D-39 | Sem inflação de reputação no OVR; reputação é campo próprio |
| D-40 | `TraitTier`: `Emerging` · `Established` · `Signature` (sem colisão com cor de carta) |
| D-41 | Tiering de trait é o instrumento de correção do débito de limiar |
| D-42 | Contagem de traits limitada por `qualityBand`, não por cor de carta |
| D-43 | Bandas de cor de carta fixadas só após corrigir o teto de OVR |
| D-44 | Camada A não-negativa; Camada B pode ser negativa |
| D-45 | Camada B tem teto maior que Camada A |
| D-46 | `cohesionModifier` dentro da faixa de Form/Mood (−5…+5) |
| D-47 | Coesão nunca toca física; entra só por `EffectiveAttributes.Read()` |
| D-48 | Em seleção, o eixo de nacionalidade é desligado |
| D-49 | Staff fora do MVP de coesão |
| D-50 | UI mostra `ScoutedCohesionView`, nunca o estado verdadeiro |
| D-51 | Agrupamento social é função pura, sem PRNG |
| D-52 | Maturidade por consequência; sem conteúdo explícito nem loop de azar |
| D-53 | Autonomia NPC↔NPC só no modo `Profundo` |
| D-54 | Modo de profundidade travado por save |
| D-55 | Menu social do MVP: `Confront` · `Support` · `RequestTransfer` · `MediaStatement` |
| D-56 | `Agent` é o modelo único de pessoa; jogador é caso particular |
| D-57 | `RefereeProfile` não é absorvido pelo `Agent` na v1 |
| D-58 | Sequência: staff → agente → imprensa → esfera pessoal, atrás de D-07/D-14 |
| D-59 | Três streams novos, entrando no fim da ordem de consumo |
| D-60 | Survey entra fatiado, com status por parte |
| D-61 | Tese do "piso legal" permanece hipótese não verificada |
| D-62 | Split em companion docs adiado até D-08 ser levantada |

---

## 10. O que isto destrava

A camada de pessoas passa a ter **decisão fechada em tudo que era decisão de design**. O que resta é o que legitimamente depende de dado empírico, de calibração contra harness, ou de outro sistema fechar primeiro — e cada um desses está nomeado na §9 com a dependência explícita.

Mais importante: a única contradição real entre artefatos (o prestígio monotônico do `DOCUMENT 3` contra a curva de fit da pesquisa) está **formalmente superada** em vez de conviver silenciosamente no ledger. Era o tipo de divergência que só aparece meses depois, quando dois sistemas calibrados contra premissas opostas começam a discordar.

**A prioridade de implementação não muda:** segue `System.Random` → SplitMix64, depois física headless. Este registro existe para que, quando a janela desta camada chegar, nenhuma decisão de design esteja bloqueando o handoff.
