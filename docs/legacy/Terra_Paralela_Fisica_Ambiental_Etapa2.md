# Terra Paralela — Camada Ambiental de Física (Etapa 2)

> Companion doc do GDD. Cross-ref: **Física de Movimentação e Bola (Etapa 1)** · **Match Engine** · **Tactics → Behavior** · **Geração de Identidade de Clube v2** (`PitchSurface`, `AtmosphereArchetype`) · **Sistema de Arbitragem** · GDD §5 (LOD).
> Convenção: prosa PT-BR, identificadores em inglês. Unidades SI. Determinismo obrigatório (SplitMix64, streams isolados).
>
> **Escopo:** clima, vento e desgaste de gramado como **moduladores** da física da Etapa 1. **Fora:** render de partículas, previsão do tempo na UI, efeito de clima sobre decisão tática.
>
> **Status de implementação:** bloqueado por **D-09** até o passo 2 de D-07 (física headless) estar verde. Este doc é design, não handoff.

---

## 0. Consumo declarado da exceção de D-08

**D-08** proíbe companion doc novo até o steering passar no harness. Este doc é escrito **assim mesmo**, e a razão precisa ficar registrada em vez de virar precedente tácito:

A Etapa 2 não é sistema novo — é a segunda metade de um módulo já em voo, encomendado no mesmo briefing da Etapa 1, e **D-09** já declarou seu design "liberado quando pedido". Foi pedido. Além disso, ela não abre superfície: fecha uma interface (`IPhysicsModifier`) que a Etapa 1 deixou declarada e não especificada — uma ponta solta é dívida maior que um doc a mais.

**A moratória volta a fechar ao fim deste documento.** Este é o único consumo da exceção. Árbitro, Tier 1.5, autoria de mundo e demais sistemas congelados permanecem congelados.

---

## 1. Fechamento das pendências da Etapa 1

O §13 da Etapa 1 tinha seis itens abertos. Todos foram fechados pelo registro de decisões de 2026-07-28. Consolidado aqui para que o módulo seja lido de um lugar só:

| Item §13 | Resolução |
| :--- | :--- |
| 1. Frequência de tick | **D-02** — física e steering a 120 Hz; `UtilityAI` a 20 Hz com histerese de 3 avaliações. |
| 2. Trilha 2D × 3D | **D-01** — 2D canônico; plano de migração 3D arquivado. |
| 3. `brakeRatio` / `turnFalloff` | **D-04** — travados como v1 (1.40 / 0.55), revisão no primeiro playtest. |
| 4. Colisão jogador × jogador | **D-03** — não existe na física; `separation` no steering, contato é evento de regra. |
| 5. Faixa de `MaxSpeed` | **D-05** — 6,5–9,5 m/s em 1–99; banda 100–120 extrapola com teto em 10,1 m/s. |
| 6. `AtmosphereArchetype` na física | **D-06** — não toca. Modificador de mando, camada de moral/decisão. |

**§13 está encerrado.** Não há mais decisão aberta na Etapa 1 — só os débitos de calibração da §9, e dois deles caem nesta sessão (§8.1).

---

## 2. Princípio da camada: modular, nunca ramificar

A tentação natural é escrever `if (weather == Rainy)` espalhado pelo solver. Isso criaria seis caminhos de código para seis climas, e depois 6 × N para clima × desgaste. Não acontece.

**D-30 — Toda variável ambiental é modulação multiplicativa de um parâmetro existente da Etapa 1. Nenhuma condição ambiental introduz ramo no solver.**

É exatamente o padrão já travado em `Tactics → Behavior`, onde tática, personalidade e atributos modulam **os mesmos pesos** por três fontes, sem lógica separada. Aqui: superfície, clima e desgaste modulam **os mesmos parâmetros físicos** por três fontes.

Consequência de teste: desligar a camada ambiental inteira (todos os escalares em 1.0) tem que reproduzir a Etapa 1 bit a bit. É o teste 1 do harness da §9.

---

## 3. A interface

```csharp
namespace SoccerSim.Core.Physics.Environment;

/// Modulador de parâmetros físicos. Toda implementação é função PURA do
/// seu próprio estado — não lê relógio, não lê PRNG, não guarda histórico.
public interface IPhysicsModifier
{
    double TractionScale(Vec2 position);      // orçamento lateral e de frenagem do jogador
    double MaxSpeedScale(Vec2 position);      // velocidade máxima
    double RestitutionScale(Vec2 position);   // restituição no quique
    double RollingScale(Vec2 position);       // resistência à rolagem
    double AirDensityScale();                 // arrasto (global por definição)
    Vec3   WindVelocity();                    // m/s, campo de ar (global por definição)
    double SurfaceNoise(Vec2 position);       // 0..1 — irregularidade; entra só no quique
}
```

Três implementações, e só três:

| Implementação | Varia no espaço? | Varia no tempo? |
| :--- | :--- | :--- |
| `SurfaceProfile` (de `stadium.pitchSurface`) | Não | Não |
| `WeatherProfile` | Não | Só o vento |
| `PitchWearMap` | **Sim** | Entre partidas |

**D-31 — A composição é multiplicativa, em ordem fixa: `SurfaceProfile → WeatherProfile → PitchWearMap`.** Multiplicação é comutativa, então a ordem hoje não altera resultado — ela é declarada mesmo assim, para que um futuro modulador não-multiplicativo não introduza ambiguidade silenciosa. `WindVelocity` é somado, não multiplicado; só o `WeatherProfile` o produz.

### 3.1 Envelope, não clamp

O produto de três escalares pode sair absurdo (0,65 × 0,80 × 0,70 = 0,36 de tração). Clampar seria fallback — proibido. A regra é a montante:

**D-32 — Cada modulador declara a faixa dos seus escalares, validada na construção. O produto é validado uma vez, na montagem da partida, contra um envelope declarado; fora dele, lança.**

```
TractionScale     ∈ [0.45, 1.05]
MaxSpeedScale     ∈ [0.85, 1.02]
RestitutionScale  ∈ [0.40, 1.15]
RollingScale      ∈ [0.60, 2.50]
AirDensityScale   ∈ [0.65, 1.10]
```

Combinação impossível não é tolerada em runtime — é bug de dado de mundo, e aparece no carregamento, não no minuto 63.

---

## 4. Clima

### 4.1 Vocabulário

**D-33 — `WeatherArchetype` é enum fechado de seis valores, fixo por partida.**

`Clear` (Ensolarado) · `Overcast` (Nublado) · `Rain` (Chuvoso) · `Snow` (Neve) · `Gale` (Ventania) · `Heatwave` (Onda de Calor)

Sem escalar contínuo de intensidade na v1. A intensidade é a própria escolha do arquétipo — mesma disciplina de `PitchSurface` e `AtmosphereArchetype`, que são enums fechados com efeito mecânico explícito por valor. Chuva leve × dilúvio vira valor novo quando houver demanda real, não parâmetro paralelo.

**D-34 — O clima não muda durante a partida na v1.** É sorteado no início e permanece. Se depois quisermos chuva no segundo tempo, **o mecanismo já existe e é grátis**: a timeline versionada de `PhysicsSettings` (Etapa 1 §6) carimba a troca no tick em que ocorre e o replay continua bit a bit. Não é uma limitação de arquitetura, é uma feature não pedida.

### 4.2 Tabela de modulação

Referência: `Clear` a 15 °C, nível do mar.

| Arquétipo | `Traction` | `MaxSpeed` | `Restitution` | `Rolling` | `AirDensity` | Vento base | `StaminaDrain`* |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `Clear` | 1.00 | 1.00 | 1.00 | 1.00 | 1.000 | 0–2 m/s | 1.00 |
| `Overcast` | 1.00 | 1.00 | 1.00 | 1.00 | 1.004 | 0–3 m/s | 0.98 |
| `Rain` | **0.80** | 1.00 | **1.05** | **0.80** | 1.018 | 2–5 m/s | 1.05 |
| `Snow` | **0.65** | 0.92 | **0.65** | **1.80** | 1.055 | 2–5 m/s | 1.15 |
| `Gale` | 0.95 | 1.00 | 1.00 | 1.00 | 1.010 | **8–14 m/s** | 1.05 |
| `Heatwave` | 1.00 | 1.00 | 0.97 | **1.15** | **0.926** | 0–2 m/s | **1.45** |

*`StaminaDrain` **não** é lido pela física — ver §4.5.

**Três leituras que valem mais que a tabela:**

**(a) Chuva deixa a bola MAIS viva, não menos.** É contraintuitivo e é medido: ensaios de rebote angular em gramado dão COR de 74–76% em superfície levemente irrigada contra 69% (bermuda TifTuf) e 56% (*Poa pratensis* em corte alto) em superfície seca. A intuição de "chuva mata a bola" está errada para chuva moderada. O que ela faz é reduzir o **atrito** — daí `RollingScale` 0,80 — e é exatamente por isso que equipes molham o gramado no intervalo: reduzir a resistência de rolagem para a bola deslizar em vez de agarrar. A não-monotonicidade real (encharcamento extremo **para** a bola) fica registrada como valor futuro do enum, não como parâmetro.

**(b) A onda de calor tem efeitos opostos por fase, e isso é riqueza de graça.** Ar a 38 °C tem densidade 1,135 kg/m³ contra 1,225 a 15 °C (lei dos gases: ρ = p/RT) — **7,4% menos arrasto**, bola voa mais longe. E gramado seco fica "agarrado", `RollingScale` 1,15 — bola rasteira morre antes. Chutão viaja, passe rasteiro trava. Nenhuma linha de código especial produziu isso: são dois escalares em parâmetros diferentes.

**(c) Nublado não faz nada, e a tabela diz isso.** `Overcast` existe como estado visual e narrativo, com efeito mecânico nulo (a diferença de densidade a 14 °C é 0,4% — abaixo do ruído). Inventar um efeito para preencher a simetria da tabela seria exatamente o tipo de número sem origem que o projeto proíbe.

### 4.3 Vento — a mudança de uma linha

Arrasto age sobre a velocidade **relativa ao ar**, não ao solo. Vento portanto não precisa de sistema: precisa que o arrasto pare de assumir ar parado.

```csharp
// Etapa 1
private Vec3 DragAcc(Vec3 v) { double speed = v.Length; ... }

// Etapa 2 — única alteração
private Vec3 DragAcc(Vec3 v)
{
    Vec3 vRel = v - _env.WindVelocity();
    double speed = vRel.Length;
    if (speed < 1e-6) return Vec3.Zero;
    double k = _s.AirDensity * _env.AirDensityScale() * _s.BallArea / (2.0 * _s.BallMass);
    return vRel * (-k * DragCoefficient(speed) * speed);
}
```

**D-35 — Vento atua exclusivamente sobre a bola.** Um jogador de 75 kg tem razão massa/área duas ordens de grandeza acima da bola; a força de arrasto sobre ele é real mas irrelevante frente à propulsão. Vento sobre jogador seria complexidade sem efeito observável.

Efeitos que **emergem** sem código dedicado:
- Chutão contra o vento perde alcance; a favor, ganha.
- Cruzamento com vento lateral encurva — e não é Magnus, é deriva de arrasto.
- A crise de arrasto passa a ser avaliada em `|v_rel|`: com vento de frente a 12 m/s, uma bola a 8 m/s vê 20 m/s de ar e **já está no regime supercrítico**. Isso é fisicamente correto e sai de graça.

### 4.4 Rajada determinística (sem transcendentais)

Vento constante é sem graça; vento senoidal viola a política de determinismo da migração SplitMix64 (`Math.Sin` proibido no caminho quente). Solução — **ruído de valor com interpolação `smoothstep`**, mesmo polinômio já usado na curva de `C_d`:

```
Waypoints a cada gustPeriod (padrão 4,0 s), amostrados de StreamName.MatchSimulation:
  w[i] = baseWind + gustAmplitude · (u[i] − 0.5) · 2      , u[i] ~ NextDouble()

Entre waypoints:
  t = (tempo − t[i]) / gustPeriod
  wind = lerp( w[i], w[i+1], t²(3 − 2t) )
```

**D-36 — A rajada consome um stream dedicado, `StreamName.Weather`, e não o stream de simulação de partida.** Adicionar valor ao enum. Motivo: se a rajada consumisse o stream da partida, mudar `gustPeriod` deslocaria toda a sequência de eventos do jogo e invalidaria replays antigos por um parâmetro cosmético. Streams isolados existem exatamente para isso.

### 4.5 Estamina não é física

**D-37 — Clima nunca escreve em `PlayerBody` diretamente. `StaminaDrain` é lido pela camada de estado do jogador, que reescreve `MaxSpeed`/`AccelMax` por fora do solver.**

A Etapa 1 já travou que os campos derivados de `PlayerBody` são preenchidos uma vez, a partir de `EffectiveAttributes.Read()`, e que o solver nunca vê atributo, forma ou fadiga. Onda de calor drenando estamina é efeito **sobre a fadiga**, e fadiga é estado de jogador. Se o clima escrevesse direto no corpo físico, teríamos duas fontes de verdade para a velocidade máxima do jogador — precisamente o erro que `EffectiveAttributes.Read()` existe para impedir.

O caminho é: `WeatherProfile.StaminaDrain` → sistema de fadiga → `EffectiveAttributes` → `PlayerBody`. Uma direção, sem atalho.

### 4.6 Altitude — gancho declarado, não implementado

O mundo tem **nações reais**. Isso significa que La Paz, Bogotá e Cidade do México existem, e que a vantagem de altitude é um fato notório do futebol sul-americano que os jogadores vão procurar.

E o custo é zero: `AirDensityScale` já existe. Atmosfera padrão dá 0,80 na Cidade do México (2.240 m), 0,77 em Bogotá (2.640 m) e **0,69 em La Paz** (3.640 m) — 31% menos arrasto, bola voando de forma visivelmente diferente.

**D-38 — Altitude entra como `stadium.altitudeMeters` no `ClubIdentity`, campo emitido e persistido, sem consumidor na v1.** Mesmo tratamento dado a `districtArchetype`: o campo entra agora porque acrescentar campo depois custa migração de schema, e o consumo entra quando o sistema de fadiga existir (altitude afeta muito mais o jogador que a bola, e o jogador é o outro sistema). Não implementar hoje evita especificar meia interface.

---

## 5. Desgaste do gramado (`PitchWearMap`)

### 5.1 Grade

**D-39 — Grade de 21 × 17 células, 5,0 × 4,0 m, cobrindo 105 × 68 m. 357 células.**

A célula tem que ser maior que a passada de um jogador (senão o desgaste vira ruído por trajetória individual) e menor que uma zona tática (senão não distingue a marca do goleiro da marca do meio-campo). 5 × 4 m dá ~2 células por área de disputa.

**Esta grade não é a grade tática.** O estado tático do Tier 1.5 usa corredores e terços; misturar as duas acoplaria dois sistemas por coincidência de resolução. Grades diferentes, propósitos diferentes.

Custo de armazenamento: 357 bytes por clube (`byte` por célula, 0–255). Uma liga de 20 clubes cabe em 7 kB. Não é preocupação.

### 5.2 Acumulação — o desgaste é cisalhamento, não quilometragem

Grama não morre porque alguém correu em linha reta sobre ela. Morre porque alguém **girou, freou ou deslizou** — força lateral arranca a raiz.

E a Etapa 1 **já calcula essa grandeza**: a elipse de tração produz `a_lat` e o componente de frenagem a cada tick. O depósito de desgaste é uma leitura de valor já computado.

```
Δw(célula do jogador) = kShear · ( |a_lat| + max(0, −a_long) ) / aRef · dtSample
```

**D-40 — Amostragem de desgaste a 1 Hz, não a 120 Hz.** Desgaste é fenômeno de escala de temporada; amostrar 120 vezes por segundo é 120× o custo para a mesma resposta. A 1 Hz, uma partida gera 5.400 amostras × 22 jogadores — trivial, e mantido no laço frio.

Uma partida sozinha move pouco o mapa. É a temporada que castiga a boca do gol e o centro do campo — e é isso que se vê em gramado real.

### 5.3 Regeneração

```
w ← max(0, w − regenRate · dias · weatherRegenFactor · facilityFactor)
```

- `weatherRegenFactor`: sol moderado e chuva leve recuperam bem; seca e geada, mal. Deriva do histórico de clima do intervalo.
- `facilityFactor`: vem das instalações/finanças do clube. É aqui que "clube pobre tem campo ruim" deixa de ser cosmético e vira mecânica — sem nenhum sistema novo.

**D-41 — `pitchSurface` é a linha de base; o desgaste é o desvio dinâmico em torno dela.** `Heavy` não é "campo desgastado" — é campo pesado e irregular por natureza, mesmo recém-recuperado. Um `Pristine` muito castigado e um `Worn` recém-tratado podem se encontrar no meio; são coisas diferentes que compõem, não sinônimos.

### 5.4 Efeito

| Grandeza | Fórmula | Máx. em `w = 1` |
| :--- | :--- | ---: |
| `RollingScale` | `1 + 0.45·w` | +45% |
| `RestitutionScale` | `1 − 0.30·w` | −30% |
| `TractionScale` | `1 − 0.25·w` | −25% |
| `MaxSpeedScale` | `1 − 0.04·w` | −4% |
| `SurfaceNoise` | `w` | — |

`MaxSpeedScale` é deliberadamente pequeno. Um jogador não corre 20% mais devagar num campo ruim — ele **escorrega e perde a bola**. O efeito real é de tração e de imprevisibilidade, e é lá que o número grande deve estar.

### 5.5 Imprevisibilidade — o quique irregular

O briefing pediu "imprevisibilidade/resistência à trajetória da bola". Resistência é `RollingScale`. Imprevisibilidade precisa de aleatoriedade, e aleatoriedade precisa de disciplina:

**D-42 — Deflexão irregular no quique, amostrada de `StreamName.Weather`, com magnitude proporcional a `SurfaceNoise` da célula de contato.**

```
No impulso de quique (Etapa 1 §5.5), após calcular v_t':
   θ  = maxDeflection · SurfaceNoise(p) · (NextDouble()·2 − 1)
   v_t' ← rotate(v_t', θ)
```

Padrão `maxDeflection` = 0,20 rad (~11,5°) em `w = 1`.

Três guardas obrigatórias:
1. **Só no quique**, nunca na rolagem. Bola rasteira desviando sozinha lê como bug, não como campo ruim.
2. **Só em `Flight → contato`**, e a rotação é do componente tangencial. A componente normal não é tocada — a bola não pula mais alto por acaso.
3. A rotação de vetor 2D por ângulo pequeno usa aproximação polinomial, **não `Math.Sin`/`Math.Cos`** — a política de determinismo da migração SplitMix64 vale aqui integralmente. Para |θ| ≤ 0,2 rad, `sin θ ≈ θ − θ³/6` e `cos θ ≈ 1 − θ²/2` têm erro abaixo de 10⁻⁵, e depois renormaliza-se o vetor para preservar a magnitude exatamente.

### 5.6 O escorregão

Um efeito, e só um, ganha tratamento próprio: sem ele, chuva e campo ruim são invisíveis para quem está assistindo.

**D-43 — `SlipEvent`: quando o fator de saturação `k` da elipse de tração excede `slipThreshold` (padrão 1,60), emitir evento e aplicar penalidade de tração por `slipTicks` (padrão 36 ticks = 0,3 s).**

Justificativa da exceção, porque é uma exceção: `k` **já é calculado** pela Etapa 1 — o escorregão é a leitura de uma grandeza existente, não um sistema novo. Custa um campo em `PlayerBody` e um ramo. E é o único canal pelo qual o jogador **percebe** a camada ambiental: sem escorregão, chuva é um número invisível numa planilha. Alinhado com o princípio já registrado de que "script feel" nasce de opacidade causal — o escorregão é a causa ficando visível.

O evento entra no `eventStream` e fica disponível para o recibo de partida narrar ("perdeu o apoio no gramado castigado da meia-lua").

---

## 6. Sorteio do clima

**D-44 — O clima é derivado de `(countryId, month)` por tabela climática, amostrado no `StreamName.Weather` na montagem da partida.**

Não é uniforme sobre os seis arquétipos: neve em julho no Brasil quebraria a ficção mais rápido que qualquer erro de física. Cada `(countryId, month)` carrega um vetor de probabilidade de seis posições que soma 1,0 (fail-fast se não somar).

**Dependência de dado nomeada** (§8.2): a tabela climática. Não é decisão de design — é dado público e abundante (normais climatológicas mensais). Entregável: 6 pesos × 12 meses por país da base de mundo.

---

## 7. Interação com o LOD

**D-45 — A camada ambiental existe somente no Tier 1. Tier 1.5, 2 e 3 não a instanciam.**

Tier 1.5 é amostragem condicionada de situações, com fronteira de abstração declarada não-negociável: nunca simulação de movimento até a situação se formar. Um `PitchWearMap` ali não teria onde ser consultado — não há posição contínua.

Se clima precisar afetar o Tier 1.5, ele entra como **variável condicionante do amostrador de situações** (junto com estado tático e placar), não como escalar de física. É outro sistema, com outra pesquisa, e está congelado.

**Consequência a registrar:** o desgaste do gramado **acumula apenas nas partidas do Tier 1**. Partidas de fundo do mesmo clube não castigam o campo. Isso é uma assimetria conhecida e aceita — o alternativa seria um modelo de desgaste estatístico paralelo, ou seja, um segundo caminho. Se virar problema visível, a correção é um incremento fixo por partida não-Tier-1, não uma segunda simulação.

---

## 8. Ancoragem e débitos

### 8.1 Dois débitos da Etapa 1 quitados

| Grandeza | Antes | Agora | Fonte |
| :--- | :--- | :--- | :--- |
| Restituição, gramado bem tratado | 0,72 **[Incerto]** | **0,69–0,76** conforme corte e umidade **[Documentado]** | Ensaio de rebote angular em bermuda TifTuf e *Poa pratensis*, dois cortes, seco e irrigado. |
| Restituição, gramado ruim | — | **0,56** **[Documentado]** | Mesmo ensaio, *Poa pratensis* a 36 mm, seco. |

Isso permite reancorar a tabela de `PitchSurface` da Etapa 1 §5.5 com origem real:

| `PitchSurface` | `RestitutionScale` | Leitura |
| :--- | ---: | :--- |
| `Pristine` | 1.00 (→ e ≈ 0,72) | Corte baixo, irrigação controlada |
| `Synthetic` | 1.05 | Sintético devolve mais |
| `Worn` | 0.88 (→ e ≈ 0,63) | Entre os dois extremos medidos |
| `Heavy` | 0.81 (→ e ≈ 0,58) | Corte alto e seco — o 0,56 medido |

O ensaio também mostra que **corte mais baixo aumenta a distância de rolagem**, o que sustenta a direção (não a magnitude) do `RollingScale` por superfície.

Débitos remanescentes da Etapa 1: `μ_r` (resistência à rolagem, **[Incerto]**), atrito de contato, `brakeRatio`/`TurnMax`/`turnFalloff` (game feel). Todos permanecem alvos do harness.

### 8.2 Novas dependências de dado

| # | Número | Origem exigida | Entregável |
| :--- | :--- | :--- | :--- |
| 4 | Tabela climática por `(countryId, month)` | Normais climatológicas mensais (dado público) | 6 pesos × 12 meses por país |
| 5 | `regenRate` de gramado | Literatura de manejo de gramado esportivo | Um float, mais fatores de clima e instalação |
| 6 | `kShear`, `aRef` do depósito de desgaste | **Não é pesquisa** — calibração contra o alvo "uma temporada produz desgaste visível" | Dois floats, fixados no harness |

O #4 é o único que precisa de coleta. O #5 tem literatura mas baixa criticidade. O #6 é calibração, não pesquisa.

---

## 9. Critérios de aceitação

Extensão do `PhysicsCalibrationHarness` da Etapa 1:

1. **Neutralidade.** Todos os moduladores em 1.0, vento zero → os 7 testes da Etapa 1 produzem hashes **idênticos** aos da Etapa 1 sem a camada. Prova que a camada é aditiva.
2. **Simetria do vento.** Chute idêntico a favor e contra vento de 10 m/s: alcances diferem em pelo menos 12 m; sem vento, o alcance fica entre os dois.
3. **Determinismo da rajada.** Mesma seed → mesma série de vento, tick a tick, em Linux e Windows. Trocar `gustPeriod` **não** altera nenhum outro evento da partida (prova o isolamento de `StreamName.Weather`).
4. **Chuva acelera a rolagem.** Passe rasteiro idêntico percorre 20–30% mais distância em `Rain` que em `Clear`.
5. **Onda de calor separa as fases.** No mesmo clima: chutão percorre 4–9% mais que em `Clear`, e passe rasteiro percorre 10–15% **menos**. Prova os efeitos opostos por fase.
6. **Acumulação de desgaste.** Temporada simulada (19 partidas em casa) produz `w > 0.4` na grande área e `w < 0.15` nos cantos. Prova que o depósito por cisalhamento discrimina zonas.
7. **Envelope.** Combinação `Heavy` + `Snow` + `w = 1` **lança** na montagem da partida, não em runtime.
8. **Deflexão limitada.** 10.000 quiques em `w = 1`: nenhum desvio angular acima de `maxDeflection`, magnitude tangencial preservada dentro de 10⁻⁶ (prova a renormalização da §5.5).

---

## 10. Build path

Ordem obrigatória, cada passo verificável e headless. **Nada começa antes de D-07 passo 2 estar verde.**

1. **`IPhysicsModifier` + `NullModifier`.** Só a interface e a identidade. Teste 1 passa. Nenhum comportamento novo.
2. **`SurfaceProfile` como primeiro implementador.** Move a tabela da Etapa 1 §5.5 para dentro da interface, com a reancoragem da §8.1.
3. **`WeatherProfile` sem vento.** Tabela da §4.2. Testes 4 e 5.
4. **Vento + rajada.** Alteração de `DragAcc`, stream `Weather`. Testes 2 e 3.
5. **`PitchWearMap`.** Grade, depósito, regeneração, persistência. Teste 6.
6. **Deflexão e `SlipEvent`.** Os dois efeitos perceptíveis, por último. Teste 8.

O segundo implementador (passo 3) é quem **confirma** que a interface do passo 1 estava certa. Se ela não servir ao clima, corrige-se ali — antes de existirem três consumidores. Abstração validada por duplicação real, não por antecipação.

---

## 11. O que fica deliberadamente fora

- **Magnus.** Continua fora (Etapa 1 §12.1). Vento produz curvatura por deriva de arrasto, que é suficiente para a v1 e não é a mesma coisa.
- **Clima variando durante a partida.** D-34. Mecanismo pronto, feature não pedida.
- **Percepção do ambiente pela IA.** O steering e o `UtilityAI` **não sabem** que está chovendo. Um jogador não decide diferente na chuva — ele é modulado pela física e escorrega. Fazer a IA perceber o ambiente é decisão da camada de decisão, tem pesquisa própria e está congelada por D-08.
- **Efeito sobre o árbitro.** Chuva altera a taxa de falta no futebol real. O `RefereeProfile` é função pura lendo contexto emitido pelo resolvedor; `WeatherArchetype` é candidato natural a entrar nesse contexto. **Não entra agora** — o sistema de arbitragem está congelado, e adicionar uma variável ao contrato dele de fora seria acoplar dois sistemas pela porta lateral.
- **Visibilidade e render.** Partículas, névoa, gramado encharcado: camada de apresentação, sem contrapartida na simulação.

---

## 12. O que isto destrava

O módulo de física está **fechado em design, ponta a ponta**: locomoção, bola, parametrização, ambiente. Nenhuma decisão de design pendente bloqueia o handoff quando D-07 passo 2 liberar.

`stadium.pitchSurface` e o futuro `stadium.altitudeMeters` ganham consumidor real. O sistema de fadiga ganha uma entrada (`StaminaDrain`) especificada antes de existir. O recibo de partida ganha dois eventos narráveis (`SlipEvent`, quique irregular) que explicam causalmente o que o jogador viu.

**A moratória D-08 volta a valer integralmente a partir daqui.**

---

## Sumário das decisões travadas neste documento

| Id | Decisão |
| :--- | :--- |
| D-30 | Variável ambiental é modulação multiplicativa; nenhum ramo novo no solver |
| D-31 | Composição em ordem fixa `Surface → Weather → Wear`; vento é somado |
| D-32 | Envelope validado na montagem da partida; sem clamp, sem fallback |
| D-33 | `WeatherArchetype` fechado em 6 valores, sem escalar de intensidade |
| D-34 | Clima fixo por partida na v1; troca já suportada pela timeline de settings |
| D-35 | Vento atua só sobre a bola |
| D-36 | Rajada consome `StreamName.Weather`, isolado do stream de partida |
| D-37 | Clima nunca escreve em `PlayerBody`; estamina passa pela camada de fadiga |
| D-38 | `stadium.altitudeMeters` emitido e persistido, sem consumidor na v1 |
| D-39 | Grade de desgaste 21 × 17 (5,0 × 4,0 m); distinta da grade tática |
| D-40 | Desgaste amostrado a 1 Hz, proporcional ao cisalhamento |
| D-41 | `pitchSurface` é linha de base; desgaste é desvio dinâmico |
| D-42 | Deflexão irregular só no quique, só no tangencial, sem transcendentais |
| D-43 | `SlipEvent` derivado do fator de saturação `k` já existente |
| D-44 | Clima sorteado de tabela climática por `(countryId, month)` |
| D-45 | Camada ambiental existe só no Tier 1; desgaste acumula só ali |
