# Terra Paralela — Tactics → Behavior (Comportamento dos Jogadores)

> Companion doc do GDD. Cross-ref: **Match Engine** (contrato `player.decide(tick) → Intention`), **Character Creation** (Personality Vector, atributos 1–99), GDD §5 (LOD), **Orquestração de Simulação** (Tier 1). Convenção: prosa PT-BR, identificadores em inglês. **D1 = Opção B.**
>
> **Escopo:** como a **tática do time** vira **decisão individual** de cada jogador, por tick. É o "cérebro" de cada jogador em campo.

---

## 1. A notícia tranquilizadora — onde isso roda

Você está certo: este é o módulo mais pesado. **Mas ele é fortemente limitado pelo LOD.**

- Ele só roda no **loop tick-a-tick de Tier 1** — e, pela escala recomendada no doc do Match Engine, isso é **só nas partidas do clube do jogador**.
- **Tiers 2/3 são estatísticos** (duplo Poisson) — **zero** IA por jogador.
- Logo, a IA pesada está limitada a **~22 agentes numa única partida por vez**, nunca o mundo inteiro.

A preocupação com recursos é real, mas **contida por design**. Não há 10.000 jogadores pensando ao mesmo tempo; há 22, na partida que você está assistindo.

---

## 2. Arquitetura de duas camadas

O split padrão de IA de esportes — **movimentação** e **decisão** são responsabilidades separadas:

| Camada | Pergunta | Técnica | Saída |
| :--- | :--- | :--- | :--- |
| **Movimentação** (`Steering`) | *Onde* eu vou? | Steering behaviors (Reynolds) | `desiredVelocity` / força |
| **Decisão** (`UtilityAI`) | *O que* eu faço com/perto da bola? | Utility AI (pontuação de ações) | `Intention` |

**Movimentação — steering behaviors (Reynolds):**
- `arrive` → chegar à posição-âncora da formação.
- `pursue` → perseguir a bola ou um jogador (antecipando posição futura).
- `interpose` → se pôr **entre dois** jogadores — marcar / bloquear linha de passe. (Reynolds usa exatamente o exemplo do futebol pra isso.)
- `separation` → não se amontoar com companheiros.
- Combinados (blend ponderado) e ancorados na formação + ajustados pela fase de jogo.

**Decisão — utility AI:**
- Cada ação possível (`passar`, `chutar`, `driblar`, `desarmar`, `conduzir`, `tocar`) recebe um **score** = soma ponderada de *considerations* normalizadas (0–1), passadas por curvas de resposta.
- Escolhe a de maior score → vira uma `Intention`.

**A tática e a personalidade modulam os PESOS das duas camadas.** Não há lógica especial espalhada; tudo é peso/parâmetro.

---

## 3. O ciclo por tick (cumpre o contrato do engine)

`player.decide(tick)`:
1. **Percebe** o estado (bola, companheiros, adversários, espaço).
2. **Movimentação** calcula a força desejada (steering combinado, âncora na formação, ajuste pela fase).
3. **Se** tem ou disputa a bola → **Decisão** pontua as ações e escolhe a `Intention`.
4. Retorna a `Intention` → vira força/evento na física e no `eventStream`.

**Determinismo:** tudo é função pura do estado; desempates e micro-variação via **PRNG custom por stream**. Fixed timestep (herdado do engine).

---

## 4. Representação da tática (`TeamTactics`)

A tática é um conjunto de **parâmetros**, não código:

- **Formation:** posições/zonas-base no campo → a âncora do `arrive`.
- **Mentality** (atacante ↔ defensivo): altura da linha, propensão a risco.
- **Roles & duties** por posição (ex.: lateral que sobe vs. lateral que fica).
- **Team instructions:** intensidade de pressão, diretividade de passe, largura, tempo (ritmo).

Cada um vira **pesos/limiares** nas duas camadas.

---

## 5. O mapeamento (o coração: Tactics → pesos)

Exemplos concretos de como cada ajuste tático modula:

- **Pressão alta** → ↑ peso da utility de "fechar/desarmar" + ↓ limiar de distância pra engajar (o `pursue`/`interpose` dispara mais cedo).
- **Passe direto** → ↑ peso de "passe longo pra frente"; **passe curto** → ↑ "toque seguro".
- **Linha alta** → ↑ a âncora da formação defensiva no campo (o `arrive` mais adiantado).
- **Largura** → espalha as âncoras laterais da formação.
- **Mentalidade atacante** → mais jogadores com âncora adiantada + ↑ peso de ações ofensivas/risco.

A tática nunca "controla" o jogador diretamente; ela **inclina os pesos**. O comportamento emerge.

---

## 6. Personalidade + atributos como modulação (o encaixe)

Aqui está a elegância: tática, personalidade e atributos modulam **os mesmos pesos**, por três fontes — sem caminhos separados.

- **Personality Vector** (já desenhado) modula os pesos: um jogador "egoísta/agressivo" tem ↑ peso de "chutar/driblar"; um "coletivo", ↑ "passar"; um "disciplinado" adere mais à âncora tática. É exatamente o padrão de utility AI (peso editável por agente).
- **Atributos (1–99)** entram nas *considerations*: a utility de "chute de longe" escala com finalização/potência; "drible" com habilidade; "passe arriscado" com visão/passe.
- **Resultado:** a **tática** define a intenção *coletiva*; **personalidade + atributos** definem como cada indivíduo a *executa* e desvia dela. Mesmos pesos, três fontes — limpo.

---

## 7. O problema da oscilação (gotcha a registrar)

Utility AI tem um problema conhecido: quando duas ações pontuam parecido, o agente fica **"twitchy"** — troca a cada frame (ex.: avançar/recuar pontuando 0.5 cada). Os jogadores tremeriam.

**Solução determinística: inércia / histerese** — dar um bônus à ação atual, ou **comprometer-se** com uma decisão por N ticks (commitment) antes de reavaliar. **Requisito obrigatório**, não opcional.

---

## 8. Mapa espacial / espaço (referência)

Pra decidir *onde* está o espaço (corridas sem bola, linhas de passe, marcação), o padrão é um **influence map** (heatmap do controle de cada time sobre as zonas do campo). As duas camadas consultam esse mapa. Entra numa versão simples primeiro (zonas) e evolui pra grid contínuo.

---

## 9. Princípios aplicados

- **Separação:** movimentação ⊥ decisão; tática/personalidade/atributos são **modulação de pesos**, não lógica separada.
- **One way:** uma fonte de verdade pros pesos (tática + personalidade + atributos combinados), um caminho de decisão.
- **Fail-fast:** tática inválida (formação incompleta, peso fora de faixa) lança erro.
- **Simple > Complex:** caminho incremental (§10).
- **Determinismo:** função pura + PRNG custom; histerese pra estabilidade.

---

## 10. Caminho incremental (build path — fiel à abstração incremental)

Não construir tudo de uma vez. Validar por duplicação real antes de abstrair:

1. **Primeiro:** um jogador, uma posição, `arrive` na âncora + utility com 2–3 ações (passe / chute / conduzir). Provar o ciclo.
2. **Depois:** marcação (`interpose`) + pressão; a **segunda** posição (defensor) confirma o padrão de modulação.
3. **Então:** extrair o núcleo (considerations + curvas de resposta reutilizáveis), validar com a **terceira** posição (meio-campo), e só aí abstrair.
4. **Por fim:** influence map, roles/duties completos, instruções de time.

---

## 11. Decisões em aberto / dependências

- Conjunto exato de ações na utility (MVP: quantas?).
- *Considerations* e curvas de resposta por ação → calibrar por **game feel**.
- Granularidade dos roles/duties (FM tem dezenas — começar com poucos).
- Influence map: versão simples (zonas) vs. contínua (grid) — começar simples.
- **Frequência de decisão:** reavaliar a utility a cada tick (60 Hz) é desperdício; padrão é **decidir em ticks mais espaçados** (ex.: a cada N frames) com a **movimentação rodando todo tick**. Fixar na implementação.
- Como a tática do time **adversário (IA)** é escolhida/adaptada (a "IA técnica" do treinador adversário — camada à parte).

---

## 12. O que isso destrava / próximo

- **Prompts de implementação:** a camada de `Steering`, a camada de `UtilityAI`, o `TeamTactics` (parâmetros), o influence map.
- Fecha o último módulo **grande** referenciado pelo Match Engine.

> Com **Orquestração** + **Match Engine** + **Tactics→Behavior** prontos no papel, e o **Árbitro** como próximo (IFAB + dados empíricos), a espinha jogável fica completa em design.
