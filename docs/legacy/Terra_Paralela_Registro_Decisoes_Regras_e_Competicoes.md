# Terra Paralela — Registro de Decisões: Regras da Partida, Competições e Personalização

> **Natureza deste documento.** Não é um companion doc novo — é um **registro de decisões** que fecha as pendências de design deixadas em aberto por *Terra Paralela — Regras da Partida, Competições e Personalização* (§8) e corrige uma ambiguidade de trilha detectada na triagem da sessão anterior. Escrito assim deliberadamente para respeitar **D-08** (moratória: nenhum companion doc novo até o steering passar no harness), seguindo o precedente já estabelecido em *Registro de Decisões: Camada de Autoria de Mundo*.
>
> **Cross-ref:** *Terra Paralela — Regras da Partida, Competições e Personalização* · *Sistema de Prestígio de Competições* (bandas B1–B6, metodologia Opta de três visões) · `Impl_Prompt_Restart_State_Machine.md` · `docs/ARCHITECTURE.md` · **D-01** (trilha canônica 2D).
> Convenção: prosa PT-BR, identificadores em inglês.
>
> **IDs deste registro usam prefixo `RC-`** (Regras e Competições) para não colidir com `D-01…D-29` já emitidos em outros registros do ledger. Diogo pode renumerar para a sequência `D-` única na consolidação, se preferir um único namespace.

---

## 1. Correção de ambiguidade — a trilha do `Impl_Prompt_Restart_State_Machine.md`

A triagem da sessão anterior sinalizou como 🟡 uma frase do prompt de implementação do Estágio 1 (*"Partida em visão lateral 2D (side-on); checagens de linha são comparações de coordenada + raio da bola"*), por suspeita de conflito com o plano de migração 3D presente nos arquivos de referência da sessão.

**Resolução: não há conflito real.** `D-01` já trava a trilha canônica como 2D, com o plano de migração 3D e o repositório `testingclaude` arquivados. A frase do prompt está **correta como escrita** — a suspeita nasceu de a triagem anterior não ter tido `D-01` disponível no contexto imediato daquela sessão, não de um erro no artefato.

**RC-01 — O prompt é corrigido apenas para citar `D-01` explicitamente**, eliminando a ambiguidade para qualquer leitura futura sem depender de memória externa. Nenhuma mudança de conteúdo técnico. Ver §5 (artefato corrigido).

---

## 2. Dimensões de campo e número de jogadores — **fora do `MatchRuleset` na v1, travado**

**RC-02 — Dimensões de campo e número de jogadores não entram como parâmetros do `MatchRuleset`.** Não é uma lacuna a preencher depois — é exclusão de escopo deliberada.

Justificativa: esses dois valores não são "regras" no sentido do §3 do doc-fonte (parâmetros que a máquina de estados lê) — são **premissas estruturais** consumidas por outros sistemas já travados: as âncoras de formação do `Tactics → Behavior` (~22 agentes por partida, conforme o próprio companion doc), os game-feel sliders (`D-04`), e o vocabulário de situação do Tier 1.5 (que assume conceitos de 11×11 — "zona", "superioridade numérica"). Mudar o número de jogadores propagaria por steering, física e taxonomia de situações simultaneamente; isso é escopo de um **modo de jogo novo** (futsal, showbol), não de um slider de ruleset. Revisitar apenas com demanda concreta de modo — mesma disciplina de abstração diferida usada em `D-25` (terceiro arquétipo de GK).

---

## 3. Prestígio de competições customizadas — **derivado, nunca autorado**

Esta era a pergunta em aberto mais próxima de um princípio já travado do projeto: *"Derived state must never be stored as truth."* OVR, valor de mercado e salário já seguem essa regra via `EffectiveAttributes.Read()`. Prestígio de competição segue o mesmo padrão — não há razão para tratá-lo diferente só por a competição ser autorada pelo jogador.

**RC-03 — O prestígio de uma competição customizada é *derivado*, computado na instanciação de cada edição, nunca escrito à mão nem persistido como verdade.**

- **Mecanismo:** reaproveita a metodologia já adotada em *Sistema de Prestígio de Competições* — as três visões da Opta (média de todos os participantes, média do top-10, média do top-5) — aplicada ao **conjunto de participantes resolvido daquela edição** (forma livre ou por `QualificationPredicate`, §6.3 do doc-fonte), lendo a banda B1–B6 de cada clube participante.
- **Consequência direta:** como os participantes de uma edição por critério podem mudar ano a ano (campeões diferentes qualificam), o prestígio da competição customizada **também varia por edição** — isso é o comportamento correto, não um bug de instabilidade. Um Mundial de Clubes que reúne só campeões de banda B1 tem prestígio alto; a mesma competição, se o predicado um dia trouxer campeões de bandas inferiores, mede mais baixo. O número reflete quem está jogando, não uma etiqueta fixa na criação.
- **Consumidor:** o mesmo `CompetitionPrestigeBand` (ou equivalente) que o resolvedor de desenvolvimento de jogador já lê para o mundo-base. Uma partida numa competição customizada de banda alta conta para a curva de fit do jogador exatamente como contaria numa competição oficial de banda equivalente — **um caminho de consumo, não um sistema de desenvolvimento paralelo para conteúdo custom.**
- **Fora de escopo aqui:** os floats exatos de peso entre as três visões e a fórmula de agregação por banda já são dependência declarada do doc de Prestígio (não deste registro) — não reabrir.

---

## 4. Recompensas e finanças de competições customizadas — **deliberadamente não decidido**

**RC-04 — Este item permanece em aberto, e a razão é diferente dos itens acima: não é decisão de design adiável por conveniência, é decisão que depende de um sistema que ainda não existe** (a economia de clubes/créditos referida como pendente no GDD). Decidir premiação agora seria inventar uma interface para um consumidor inexistente — exatamente o erro que `D-19` (`DistrictArchetype`) evitou ao declarar "campo emitido, zero efeito" em vez de adivinhar.

Quando o sistema de economia existir: uma competição customizada declara um `prizePool` opcional, consumido pelo resolvedor financeiro daquele sistema — nunca antes disso.

---

## 5. Disposição de clubes de IA a convites — **v1 travada em participação compulsória**

**RC-05 — Na v1, um clube que satisfaz a qualificação (forma livre ou `QualificationPredicate`) participa. Não há recusa, negociação de calendário ou avaliação de prestígio pelo clube-IA.**

Justificativa: é a mesma disciplina de *deferred abstraction* já usada no projeto — construir o caminho simples primeiro, generalizar só no segundo caso de uso real. Simular "vontade" de um clube exige um modelo de relacionamento/narrativa que não está desenhado; forçar a decisão agora criaria a interface errada. Um clube que recusaria por lotação de calendário é resolvido de forma mais simples e correta pelo RC-06 abaixo — falha na criação, não recusa em runtime.

---

## 6. Conflito de calendário — **fail-fast na criação, sem renegociação automática**

**RC-06 — Se a janela declarada de uma edição colide com uma obrigação de calendário já fixada (fixture oficial de liga/copa) de qualquer clube no conjunto de participantes resolvido, a instanciação da edição lança erro.** Nenhuma tentativa automática de reencaixe, nenhum adiamento silencioso de partida oficial.

Justificativa direta: "one way, no fallbacks" aplicado ao calendário. O jogador vê o erro na criação e ajusta a janela ou o escopo de participantes — o mesmo padrão de fail-fast já usado em elegibilidade de escalação (§5.4 do doc-fonte) e em qualificação insuficiente (§6.3). Evita o caso mais frágil possível: um motor de renegociação de calendário tentando satisfazer N competições simultaneamente sem regra determinística de prioridade.

---

## 7. Rulesets experimentais (sin-bin, cartão azul, etc.) — **confirmado como conteúdo, não arquitetura nova**

**RC-07 — Nenhuma decisão de arquitetura é necessária para esses candidatos.** A camada de sanção (§2.2 do doc-fonte, `RefereeProfile` → classificação de desfecho) já é aberta a novos desfechos de classificação sem mudança estrutural — um sin-bin é só mais um valor de sanção que a máquina de estados de reinícios já sabe aplicar (jogador temporariamente fora, retorno programado). Confirmado como backlog de conteúdo pós-v1, não como item bloqueando implementação.

---

## 8. O que permanece genuinamente em aberto (research-gated, não decidido aqui — corretamente)

Estes **não** recebem decisão de design porque decidi-los agora violaria "research before thresholds":

1. **Vocabulário de contexto de falta compartilhado entre Tier 1 e Tier 1.5** — bloqueado pelo retorno de `Research_Prompt_Taxonomia_Situacoes_Estado_Tatico.md` (Eixo D).
2. **Limiares numéricos do `RefereeProfile`** (severidade de contato, distância DOGSO, contagem de infração persistente) — bloqueados pela pesquisa de IFAB + comportamento empírico de árbitros.

Nenhum número entra aqui. Isso é o registro reconhecendo seu próprio limite, não uma lacuna esquecida.

---

## 9. Artefato corrigido nesta sessão

`Impl_Prompt_Restart_State_Machine.md` — item de contexto 4 atualizado para citar `D-01` explicitamente (RC-01). Reemitido junto a este registro.

---

### Sumário das decisões travadas neste registro

| Id | Decisão |
| :--- | :--- |
| RC-01 | Ambiguidade 2D/3D do prompt do Estágio 1 resolvida — `D-01` já travava 2D; prompt corrigido só para citar a decisão explicitamente |
| RC-02 | Dimensões de campo / nº de jogadores fora do `MatchRuleset` na v1 — premissa estrutural, não parâmetro |
| RC-03 | Prestígio de competição customizada é derivado por edição (metodologia de três visões já adotada), nunca autorado nem persistido |
| RC-04 | Premiação/finanças de competição customizada deliberadamente não decidida — depende de sistema de economia inexistente |
| RC-05 | v1: participação compulsória para clubes qualificados; sem recusa/negociação narrativa |
| RC-06 | Conflito de calendário na criação de edição → fail-fast, sem renegociação automática |
| RC-07 | Rulesets experimentais confirmados como conteúdo sobre arquitetura existente, sem mudança estrutural necessária |
