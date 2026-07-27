# Lista de Assets — Etapa 1 (Fundação Visual + UI)

Documento de produção para encomendar/produzir os assets da **primeira etapa** do
[plano game-ready](GAME_READY_PLAN.md) — Fases 0 e 1 (fundação visual e migração da UI
para cenas do editor). É o material necessário para o jogo ter identidade própria e uma
interface real; a arte de partida e do life-sim vem nas etapas seguintes (ver [Anexo](#anexo--prévia-das-próximas-etapas)).

**Total desta etapa: ~43 arquivos.** Nenhum modelo 3D e nenhum vídeo são necessários —
o jogo é 2D pixel art e a Godot 4 só reproduz vídeo em Ogg Theora, formato que não
pretendemos usar.

---

## 1. Fundamento técnico: por que 640×360

A resolução base define o "contrato" de todo o resto. Escolhemos **640×360** (16:9)
porque ela é múltiplo inteiro exato das três resoluções que dominam o mercado de PC.
Dados da [Steam Hardware Survey](https://store.steampowered.com/hwsurvey/Steam-Hardware-Software-Survey-Welcome-to-Steam) (2026):

| Resolução do jogador | Fatia dos usuários Steam | Escala a partir de 640×360 | Resultado |
| --- | --- | --- | --- |
| 1920×1080 (Full HD) | **~51%** | ×3 | Perfeito, tela cheia |
| 2560×1440 (QHD) | **~21%** | ×4 | Perfeito, tela cheia |
| 3840×2160 (4K) | **~5%** | ×6 | Perfeito, tela cheia |
| 1366×768 (notebooks) | ~3% | ×2 (1280×720) | Pequenas barras laterais |
| Ultrawide 21:9 | ~2% | ×4/×6 | Barras laterais (pillarbox) |

**~78% dos jogadores rodam o jogo em escala inteira perfeita** — cada pixel da arte vira
um quadrado exato de 3×3, 4×4 ou 6×6 pixels na tela, sem borrão e sem distorção. É o
principal motivo técnico para pixel art parecer nítida em vez de "lavada".

Configuração na Godot (Fase 0): `viewport 640×360`, `stretch/mode = viewport`,
`stretch/aspect = keep`, filtro de textura `Nearest`.

### Regra da "área segura" (importante para os fundos)

Para não termos que redesenhar fundos caso decidamos preencher telas ultrawide no futuro:

> Desenhe as artes de fundo em **704×396**, mas mantenha todo elemento importante
> (logo, texto, foco visual) dentro do retângulo central de **640×360**. A moldura extra
> é sangria (bleed) descartável.

Isso vale só para os fundos de tela cheia (grupo D). Todo o resto é desenhado na grade de 640×360.

---

## 2. Regras gerais de formato

| Tipo de mídia | Formato exigido | Especificação |
| --- | --- | --- |
| **Imagem** (sprites, UI, fundos) | **PNG-24 com alfa** (RGBA) | Sem entrelaçamento, sem perfil de cor embutido (assumir sRGB). **Sem antialiasing** nas bordas — pixel art precisa de transparência binária (0% ou 100%), meio-tom só dentro do desenho. |
| **Vetor** (só o ícone do editor) | **SVG** | ViewBox quadrado, sem fontes externas. |
| **Fonte** | **TTF** pixel-perfect ou **.fnt** bitmap | Precisa renderizar nítida no tamanho nominal sem hinting. **Obrigatório: acentuação completa PT-BR** (á à â ã é ê í ó ô õ ú ü ç) e caracteres EN. |
| **Efeito sonoro** | **WAV** PCM 16-bit | 44.100 Hz, **mono**. Sem compressão (a Godot importa direto, sem custo de decodificação). |
| **Música / ambiente** | **OGG Vorbis** | 44.100 Hz, **estéreo**, qualidade ~5 (≈160 kbps). **Loop perfeito** (o fim emenda no início sem clique). |
| **Vídeo** | *Não usar nesta etapa* | A Godot 4 só suporta Ogg Theora (.ogv), de qualidade ruim. |
| **Modelo 3D** | *Não aplicável* | Jogo 100% 2D. |

**Nível de áudio:** picos normalizados a −3 dBFS; música com loudness alvo em torno de
−16 LUFS; efeitos de UI bem discretos (não podem cansar em uso repetido).

---

## 3. Paleta de cores

Estilo alvo: **16-bit** (era SNES/Mega Drive). Restrição recomendada: **paleta única de
32 a 48 cores** para o jogo inteiro, garantindo coesão visual entre UI, partida e life-sim.

Sugestões de paletas prontas e livres, caso queira partir de uma base testada:
**DawnBringer 32 (DB32)**, **Endesga 32**, ou **AAP-64** (se precisar de mais nuance).

Requisitos funcionais da paleta:
- Uma rampa de **verdes** (gramado, 5+ tons) — usada intensamente na Fase 2.
- Uma rampa **neutra** (cinzas/marrons) para UI e arquibancada.
- Duas cores de **destaque** contrastantes para times mandante/visitante.
- Cor de **erro/alerta** e cor de **sucesso/gol**.
- **Contraste mínimo 4.5:1** entre texto e fundo (legibilidade — o jogo é cheio de texto).

Entregar a paleta final como `docs/palette.png` (uma faixa de swatches) e/ou `.gpl` (GIMP/Aseprite).

---

## 4. Nomenclatura e estrutura de pastas

```
game/assets/
├── fonts/          A. Tipografia
├── ui/
│   ├── theme/      B. 9-slices e widgets
│   └── icons/      B. Ícones de 16×16
├── brand/          C. Logo, ícone, splash
├── backgrounds/    D. Fundos de tela
├── audio/
│   ├── sfx/        E. Efeitos
│   └── music/      E. Música e ambiente
└── cursors/        F. Cursores (opcional)
```

**Convenção de nomes:** `snake_case`, sempre minúsculo, sem acentos e sem espaços.
Padrão `categoria_nome_estado.png` (ex.: `btn_hover.png`).
Sequências de animação usam sufixo numérico de 2 dígitos (`player_run_00.png`).

---

## 5. Lista de assets

Prioridade: **P0** = bloqueia o desenvolvimento · **P1** = necessário para a etapa fechar ·
**P2** = desejável, pode vir depois.

### Grupo A — Tipografia

| ID | Arquivo | Especificação | Tipo | Descrição | Prio |
| --- | --- | --- | --- | --- | --- |
| A1 | `font_body.ttf` | Corpo nominal **8 px** | Fonte | Texto geral: menus, feed de eventos, tabelas, diálogos. Precisa ser legível numa linha de ~70 caracteres. | **P0** |
| A2 | `font_title.ttf` | Corpo nominal **16 px** | Fonte | Títulos, nome do jogo, placar da partida. Pode ser a mesma família em bold/dobro. | **P0** |
| A3 | `font_mono.ttf` | Corpo **8 px**, monoespaçada | Fonte | Tabelas de classificação e box score, onde as colunas precisam alinhar. | P2 |

> **Recomendação pronta e gratuita:** a família **Pixel Operator** (domínio público / CC0)
> cobre os três casos — tem variantes 8 px, 16 px, Bold e Mono, e acentuação latina
> completa. Alternativas: *m6x11*, *Silver*, *Press Start 2P* (esta última só caixa alta,
> serve só para título). Se optar por fonte comercial, confirmar licença de redistribuição
> embutida no jogo.

### Grupo B — Tema de interface

Os itens marcados "9-slice" são imagens pequenas que a Godot estica sem deformar as
bordas — por isso a arte é minúscula e a **margem** precisa ser respeitada.

| ID | Arquivo | Especificação | Tipo | Descrição | Prio |
| --- | --- | --- | --- | --- | --- |
| B1 | `btn_normal.png` | 16×16, 9-slice margem 5 px | Imagem | Botão em repouso. | **P0** |
| B2 | `btn_hover.png` | 16×16, margem 5 px | Imagem | Botão sob o mouse **ou com foco de teclado**. | **P0** |
| B3 | `btn_pressed.png` | 16×16, margem 5 px | Imagem | Botão pressionado. | **P0** |
| B4 | `btn_disabled.png` | 16×16, margem 5 px | Imagem | Botão inativo (ex.: "Pular" após o fim do jogo). | **P0** |
| B5 | `panel_bg.png` | 16×16, margem 5 px | Imagem | Caixa/painel de conteúdo elevado. | **P0** |
| B6 | `panel_inset.png` | 16×16, margem 5 px | Imagem | Área rebaixada: feed de eventos, listas roláveis. | P1 |
| B7 | `focus_outline.png` | 16×16, margem 5 px | Imagem | Contorno de foco para navegação por teclado/controle (acessibilidade). | P1 |
| B8 | `scrollbar_track.png` | 8×8, margem 3 px | Imagem | Trilho da barra de rolagem vertical. | P1 |
| B9 | `scrollbar_grabber.png` | 8×8, margem 3 px | Imagem | Cursor arrastável da barra. | P1 |
| B10 | `separator.png` | 8×2 | Imagem | Linha divisória horizontal. | P2 |
| B11 | `tooltip_bg.png` | 16×16, margem 5 px | Imagem | Fundo de dica flutuante. | P2 |
| B12 | `progress_track.png` + `progress_fill.png` | 16×8, margem 4 px cada | Imagem | Barra de progresso (carregamento, avanço de calendário). | P2 |

**Ícones de UI** — todos **16×16 px**, monocromáticos com no máximo 1 cor de destaque,
legíveis quando ampliados ×3:

| ID | Arquivos | Tipo | Descrição | Prio |
| --- | --- | --- | --- | --- |
| B13 | `icon_play.png`, `icon_pause.png`, `icon_ff_2x.png`, `icon_ff_4x.png` | Imagem | Controles de tempo (já existem no `GameModeManager`: pausa e fast-forward 2×/4×). | P1 |
| B14 | `icon_back.png`, `icon_settings.png`, `icon_close.png` | Imagem | Navegação e opções. | P1 |
| B15 | `icon_calendar.png`, `icon_ball.png`, `icon_home.png` | Imagem | Os três destinos do menu: calendário, partida, vida. | P1 |

### Grupo C — Identidade visual

| ID | Arquivo | Especificação | Tipo | Descrição | Prio |
| --- | --- | --- | --- | --- | --- |
| C1 | `logo_full.png` | Máx. **320×96**, alfa | Imagem | Logotipo com o nome do jogo. Usado no menu principal e no splash. | **P0** |
| C2 | `logo_mark.png` | **64×64**, alfa | Imagem | Só o símbolo (escudo/bola), sem texto — para cantos de tela e marca d'água. | P1 |
| C3 | `icon.svg` | Vetor, viewBox quadrado | Vetor | Ícone exibido pelo editor Godot (substitui o `icon.svg` genérico atual). | P1 |
| C4 | `icon_1024.png` | **1024×1024** | Imagem | Arte-mestra do ícone; dela geramos os demais tamanhos. | P1 |
| C5 | `app_icon.ico` | Multi-resolução: 16, 32, 48, 64, 128, 256 | Imagem | Ícone do executável Windows (exigido no export). | P2 |
| C6 | `splash.png` | **640×360** | Imagem | Tela de abertura da Godot (logo sobre fundo sólido). | P2 |

### Grupo D — Fundos de tela

Todos em **704×396** com a área segura central de 640×360 (ver §1).

| ID | Arquivo | Tipo | Descrição | Prio |
| --- | --- | --- | --- | --- |
| D1 | `bg_menu.png` | Imagem | Fundo do menu principal. Sugestão temática: estádio ao entardecer, arquibancada desfocada, ou o vestiário. Precisa ter área de baixo contraste onde o logo e os botões ficam. | **P0** |
| D2 | `bg_calendar.png` | Imagem | Fundo da tela de calendário — mais sóbrio, tipo mesa/agenda, para não competir com o texto. | P1 |
| D3 | `bg_neutral_dark.png` | Imagem | Fundo neutro reutilizável (telas ainda sem arte dedicada, diálogos, opções). | P1 |
| D4 | `hud_scoreboard_strip.png` | **640×32**, alfa | Imagem | Faixa do placar sobreposta na tela de partida. | P2 |

### Grupo E — Áudio

**Efeitos de interface** — WAV, mono, 44,1 kHz, 16-bit:

| ID | Arquivo | Duração | Tipo | Descrição | Prio |
| --- | --- | --- | --- | --- | --- |
| E1 | `ui_click.wav` | 80–150 ms | Som | Confirmação de botão. Curto e seco. | **P0** |
| E2 | `ui_hover.wav` | 40–80 ms | Som | Foco/passagem do mouse. **Muito** discreto — toca o tempo todo. | P1 |
| E3 | `ui_back.wav` | ~100 ms | Som | Voltar/cancelar. Tom descendente. | P1 |
| E4 | `ui_error.wav` | ~150 ms | Som | Ação inválida (ex.: "Jogar próxima partida" sem partida pendente). | P1 |
| E5 | `ui_transition.wav` | ~120 ms | Som | Troca de tela/modo. | P2 |

**Música e ambiente** — OGG Vorbis, estéreo, em loop perfeito:

| ID | Arquivo | Duração | Tipo | Descrição | Prio |
| --- | --- | --- | --- | --- | --- |
| E6 | `mus_menu.ogg` | 60–120 s | Som | Tema do menu principal. Deve estabelecer o tom do jogo (esportivo, nostálgico). | P1 |
| E7 | `mus_calendar.ogg` | 90–180 s | Som | Trilha do calendário/vida — calma, pouco intrusiva, feita para ouvir por muito tempo. | P2 |
| E8 | `amb_crowd_loop.ogg` | 30–60 s | Som | Murmúrio de torcida. Já é útil aqui e vira base da Fase 2. | P2 |

### Grupo F — Cursores (opcional)

| ID | Arquivo | Especificação | Tipo | Descrição | Prio |
| --- | --- | --- | --- | --- | --- |
| F1 | `cursor_default.png` | 16×16, hotspot (0,0) | Imagem | Ponteiro padrão estilizado. | P2 |
| F2 | `cursor_pointer.png` | 16×16, hotspot (5,0) | Imagem | Mãozinha sobre elementos clicáveis. | P2 |

---

## 6. Critérios de aceite técnico

Checklist de conferência na entrega de cada lote:

- [ ] Dimensões **exatamente** iguais às da tabela (a grade de pixel não perdoa 1 px a mais).
- [ ] PNG com canal alfa; transparência **binária** nas bordas (sem pixels semitransparentes de antialiasing).
- [ ] Nenhuma cor fora da paleta definida em §3.
- [ ] 9-slices com as margens respeitadas — o miolo precisa ser esticável sem revelar detalhe.
- [ ] Fontes renderizam nítidas em 8 px/16 px, **com acentos PT-BR conferidos** (`ação`, `José`, `pênalti`, `único`).
- [ ] Áudio: WAV mono 16-bit/44,1 kHz para SFX; OGG estéreo para música; sem clique no ponto de loop.
- [ ] Picos em −3 dBFS; nenhum arquivo clipando.
- [ ] Nomes em `snake_case`, nas pastas corretas.
- [ ] Licença de cada asset de terceiros registrada em `game/assets/CREDITS.md`.

---

## 7. Riscos e decisões em aberto

**Densidade de texto em 640×360.** Este é um jogo de simulação com bastante texto
(calendário, feed da partida, box score, diálogos). A 640×360 com fonte de 8 px cabem
cerca de 70 caracteres por linha e ~20 linhas úteis — apertado para tabelas densas.
*Plano B, se doer na Fase 1:* manter o mundo renderizado a 640×360 e colocar a UI num
`CanvasLayer` separado em resolução maior. Isso **não** invalida nenhum asset desta
lista, apenas permite mais linhas de texto por tela. Decisão adiada de propósito até
termos telas reais para julgar.

**Direção de arte ainda não definida.** As dimensões estão travadas; o *estilo*
(realista-pixel vs. cartunesco, época retrô vs. contemporânea) não. Recomendo fechar o
D1 (`bg_menu.png`) e o C1 (`logo_full.png`) primeiro — eles definem o tom e orientam
todo o resto.

**Placeholders não bloqueiam o código.** Posso gerar um kit de placeholders
programaticamente (formas sólidas nas dimensões corretas, bipes sintetizados) para as
Fases 0 e 1 avançarem em paralelo com a produção de arte. É só pedir.

---

## Anexo — Prévia das próximas etapas

Não encomende agora, mas compartilhe com quem for produzir a arte: saber o destino evita
retrabalho de estilo.

**Etapa 2 — Partida renderizada (Fase 2):** tiles de gramado 32×32 (com faixas de corte,
linhas, marca do meio, grande área), arquibancada e traves; jogadores em **32×48 px** por
quadro, visão lateral, com animações *parado / correndo / chutando / comemorando* (4–8
quadros cada) e paleta trocável por time; bola **8×8** com sombra; partículas de chuva;
sons de chute, apito, rede e reação de torcida.

**Etapa 3 — Life-sim isométrico (Fase 3):** tiles isométricos **64×32** (proporção 2:1)
para cômodos e rua; personagem **32×48** em 4 direções (parado/andando); mobiliário e
objetos interativos; retratos de diálogo.

**Escala mundo→tela da partida:** 8 pixels por metro (campo de 105 m = 840 px de largura,
com a câmera acompanhando a bola). Note que os sprites são **propositalmente fora de
escala física** — um jogador de 1,80 m mediria 14 px se fosse literal, o que seria
ilegível. A *posição* vem da simulação em metros; o *tamanho* do sprite é direção de arte.
