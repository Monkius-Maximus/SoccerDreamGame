# Plano "Game Ready" — da fundação jogável ao build distribuível

Este documento planeja a transformação do projeto em um jogo apresentável, seguindo um
princípio único: **adaptar tudo que é apresentação para os sistemas nativos da Godot, e
não tocar no núcleo determinístico** (`src/SoccerSim.Core` continua engine-agnóstico,
testado headless — a regra de ouro do [`ARCHITECTURE.md`](ARCHITECTURE.md)).

O que a cena renderizada faz é **observar** a simulação, nunca decidi-la.

---

## Invariantes (valem para todas as fases)

- `dotnet test tests/SoccerSim.Core.Tests` verde em todo commit.
- Nenhum tipo da Godot entra em `src/`. Nenhuma regra de jogo entra em `game/`.
- O `MatchSimulation.Step()` continua sendo o único portão de avanço da partida —
  o modo headless (calendário) e o modo renderizado rodam a MESMA matemática.
- Toda mudança de cena/modo continua passando pelo `GameModeManager`.

---

## Fase 0 — Fundação visual (destrava a produção de assets)

Hoje o projeto roda no default da Godot (janela 1152×648, sem escala de pixel art,
sem tema). Esta fase define o "contrato visual" do jogo inteiro.

### 0.1 Decisões a tomar (propostas)

| Decisão | Proposta | Racional |
| --- | --- | --- |
| Resolução base | **640×360** | Estilo 16-bit; escala inteira exata para 720p (×2), 1080p (×3), 4K (×6). |
| Escala de pixel art | `stretch/mode = viewport`, `aspect = keep` | A Godot escala o jogo inteiro; pixels sempre nítidos e uniformes. |
| Filtro de textura | `Nearest` (default do projeto) | Sem blur em sprites. |
| Tile da partida (side-on) | **32×32 px** | Gramado, arquibancada, linhas. |
| Tile do life-sim (isométrico) | **64×32 px** (proporção 2:1) | Padrão isométrico da `TileMapLayer`. |
| Sprite de jogador (partida) | **32×48 px** por frame | Legível em 640×360 com ~10 jogadores em tela. |
| Sprite de personagem (life-sim) | **32×48 px** por frame | Reuso de proporção; 4 direções. |
| Bola | **8×8 px** | Estilizada (maior que a escala real, para leitura). |
| Fonte | Bitmap 8 px (+ título 16 px) | Nítida nas escalas inteiras. |
| Escala sim→tela da partida | **8 px por metro** (campo 105 m → 840 px de largura; a câmera rola) | Definida em UM lugar (`PitchView`), calibrável. |

> Os sprites são deliberadamente FORA de escala física (um jogador de 1,80 m a
> 8 px/m teria ~14 px — ilegível). A POSIÇÃO vem da simulação em metros; o
> TAMANHO do sprite é direção de arte.

### 0.2 Entregas

- [ ] `project.godot`: seção `[display]` (resolução base, stretch, aspect) e
      filtro `Nearest` como default de textura.
- [ ] `docs/ART_SPEC.md`: a tabela acima expandida — dimensões canônicas, paleta
      (limite de cores estilo 16-bit), nomenclatura de arquivos
      (`player_run_side_00.png`…), estrutura de pastas `game/assets/`.
- [ ] `game/assets/theme/theme.tres`: `Theme` único do projeto (fonte bitmap,
      cores de botão/label/painel) aplicado via Project Settings → GUI Theme.
- [ ] Assets placeholder gerados por nós (retângulos/formas nas dimensões canônicas)
      para as Fases 1–3 não dependerem de arte final.

**Critério de aceite:** o jogo abre em 640×360 escalado inteiro, menu com tema
aplicado, tudo nítido em qualquer resolução de janela.

---

## Fase 1 — UI nativa do editor (parar de montar interface por código)

Hoje `MainMenu.cs`, `CalendarScene.cs` e `MatchScene.cs` constroem a UI inteira em
`BuildUi()` com `new Label`/`new Button`. A Godot faz isso melhor no editor.

### Entregas

- [ ] `MainMenu.tscn`: layout no editor (containers, âncoras), botões conectados por
      sinal no editor; `MainMenu.cs` mantém só os handlers (`OnPlayNextFixturePressed`…).
- [ ] `CalendarScene.tscn`: idem (data, avanço de 14 dias, voltar).
- [ ] `MatchScene.tscn`: HUD do ticker (placar, relógio, feed, box score, botões)
      como nós do editor; o script mantém só a lógica de replay.
- [ ] `LifeSimScene.tscn`: esqueleto com o mesmo padrão.
- [ ] `InputMap` (Project Settings → Input Map): ações `pause`, `speed_2x`,
      `speed_4x`, `skip_match`, `back_to_menu` — o código passa a checar ações,
      nunca teclas.
- [ ] Localização nativa: textos visíveis viram chaves `tr()`, com
      `game/i18n/strings.csv` em **pt_BR** e **en**; idioma configurável.

**Critério de aceite:** nenhuma cena instancia `Control` por código (exceto linhas
dinâmicas do feed de eventos); trocar o idioma troca todos os textos.

---

## Fase 2 — Partida renderizada de verdade (o coração do jogo)

Hoje a `MatchScene` roda a partida headless e "reconta" o event stream como ticker.
Nesta fase ela passa a **observar a simulação tick a tick**, desenhando campo, 22
jogadores e bola. Nada da física/decisão muda — muda quem desenha.

### Arquitetura da cena

```
MatchScene (Node2D)
├── TileMapLayer            ← campo (tiles 32×32; linhas, gramado listrado)
├── Node2D "Actors"
│   ├── 22 × AnimatedSprite2D  ← jogadores (idle/run/kick), flip por direção
│   └── Sprite2D "Ball" + sombra ← altura (Y físico) vira offset+escala da sombra
├── Camera2D                ← segue a bola; limites = bordas do campo; smoothing
├── GPUParticles2D "Rain"   ← ligada a MatchConditions.RainIntensity
├── CanvasLayer "HUD"       ← placar/relógio/feed da Fase 1 (reaproveitados)
└── AudioStreamPlayer(s)    ← torcida (loop), apito, chute, gol
```

### Entregas

- [ ] **Modo observado no core**: `MatchPresentationService` ganha, além do
      `Play()` headless atual, um modo streaming que entrega a `MatchSimulation`
      viva (a cena chama `Step`); a persistência do resultado continua acontecendo
      exatamente uma vez, ao fim.
- [ ] **`PitchView` (game/)**: o ÚNICO ponto de conversão metros→pixels
      (8 px/m) e da projeção side-on: `X` físico → `x` tela; `Z` (largura do
      campo) → profundidade em `y` + ordenação de desenho (`YSort`); `Y` físico
      (altura da bola) → offset do sprite + escala da sombra.
- [ ] **Loop de render**: `_PhysicsProcess` (60 Hz, casa com `FixedDt = 1/60`)
      chama `Step()` e reposiciona os nós lendo o estado pós-tick. Fast-forward
      já funciona de graça: `Engine.TimeScale` (do `GameModeManager`) acelera o
      `_PhysicsProcess`.
- [ ] **Skip**: botão "pular" roda a simulação restante headless no mesmo objeto
      (o `Step` é o mesmo) e corta para o box score.
- [ ] **Animação**: mapear estado→animação (parado/correndo/chutando) via
      `AnimatedSprite2D`; sem lógica de jogo aqui.
- [ ] **Clima**: chuva por partículas quando `RainIntensity > 0`.
- [ ] **Áudio**: buses `Music`/`SFX`/`Crowd`; eventos do stream disparam sons
      (gol, apito de início/fim).

**Critério de aceite:** apertar "Play Next Fixture" mostra a partida acontecendo
em campo com câmera na bola, a 60 Hz, com placar/relógio; o resultado persistido
é IDÊNTICO ao que o modo headless produziria com a mesma seed (teste de paridade).

---

## Fase 3 — Life-sim isométrico jogável

O modo vida ganha seu primeiro loop: andar pelo espaço, interagir, gastar tempo.
Aqui a física da Godot é PERMITIDA (movimento de personagem não precisa ser
determinístico — não é replay de simulação).

### Entregas

- [ ] `TileMapLayer` isométrico (64×32) com a casa/bairro inicial; oclusão por `YSort`.
- [ ] Personagem `CharacterBody2D` + `AnimatedSprite2D` (4 direções), movimento
      pelo `InputMap`.
- [ ] Interações por `Area2D` (cama = dormir/avançar tempo, porta = sair,
      TV = descanso): cada interação chama os serviços do core
      (`TimeManager.SkipToTaskCompletion`, deltas de FormMood via evento).
- [ ] Integração com o calendário: sair do life-sim → `GameModeManager.EnterCalendar()`
      (transições já existem na state machine).
- [ ] Eventos High/Medium interrompendo de verdade: o `EventBusNode` troca o
      `Task.FromResult` no-op por uma cena de diálogo simples (a primeira
      resolução interativa de evento).

**Critério de aceite:** um dia completo jogável — acordar, interagir, jogar a
partida do dia, avançar o calendário — sem tocar em código.

---

## Fase 4 — Polimento e distribuição

- [ ] Menu de opções: volume por bus, idioma, janela/fullscreen (persistidos em
      `user://settings.cfg` via `ConfigFile` — nativo da Godot).
- [ ] Menu de pause global (ESC) usando o `Pause()/Resume()` já existentes no
      `GameModeManager`.
- [ ] Transições de cena com `Tween`/fade (polimento do `SwitchTo`).
- [ ] Ícone e nome do executável; splash.
- [ ] Export presets **Windows** e **Linux** (Project → Export). *Limitação
      conhecida: export Web com C#/.NET ainda não é suportado pela Godot 4.x —
      foco em desktop.*
- [ ] CI (GitHub Actions): `dotnet test` + `dotnet build` em todo push.

**Critério de aceite:** um zip de Windows que um amigo baixa, abre e joga.

---

## Ordem e dependências

```
Fase 0 (contrato visual)
  └─→ Fase 1 (UI no editor — usa o tema/resolução)
        └─→ Fase 2 (partida renderizada — usa HUD, assets, PitchView)
        └─→ Fase 3 (life-sim — usa personagem, tiles, InputMap)
              └─→ Fase 4 (polimento/export — precisa de tudo acima)
```

Fases 2 e 3 são paralelizáveis após a Fase 1. Cada checkbox é pensado para ser um
commit/PR pequeno e verificável.

## Riscos / decisões em aberto

1. **Direção de arte real** — o plano usa placeholders; arte final (própria,
   encomendada ou asset pack) pode ajustar as dimensões do `ART_SPEC.md`
   (por isso tudo referencia o spec, não números espalhados).
2. **Projeção da visão side-on** — quanta profundidade (eixo Z) mostrar é decisão
   de game feel; o `PitchView` isola isso para iterarmos barato.
3. **Performance do tick 60 Hz + render** — 22 brains por tick é leve hoje
   (placeholder), mas o brain real pode pesar; se precisar, o core já permite
   rodar o `Step` em thread e a cena interpolar (decisão adiada de propósito).
