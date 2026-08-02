namespace SoccerSim.Core.Localization;

/// <summary>
/// The display strings, per locale.
///
/// <para>
/// Lives in Core rather than as a Godot resource for two reasons: it needs no engine (a catalogue is
/// a dictionary), and keeping it here makes key coverage unit-testable — a test can assert that
/// every key the simulation can produce resolves in every locale, which is the failure mode that
/// otherwise only shows up as a raw <c>activity.sleep.name</c> on someone's screen.
/// </para>
///
/// <para>
/// <b>pt-BR is the source of truth</b>, because that is the language the game is written in; English
/// exists so the code's identifiers have an obvious counterpart and so a future translator has a
/// pivot. Moving this to <c>.po</c> files later is mechanical — the keys do not change.
/// </para>
/// </summary>
public static class StringCatalogue
{
    /// <summary>The locale the game ships in.</summary>
    public const string DefaultLocale = "pt-BR";

    /// <summary>Resolved when the active locale lacks a key.</summary>
    public const string FallbackLocale = "en";

    /// <summary>Every locale this build carries.</summary>
    public static IReadOnlyList<string> Locales { get; } = [DefaultLocale, FallbackLocale];

    // Lazy, not a plain initializer: static initializers run in declaration order, and this map is
    // declared above the per-locale dictionaries it references. Building it eagerly would capture
    // them while they are still null. Deferring to first access sidesteps the ordering entirely.
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> AllLazy =
        new(() => new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultLocale] = BuildPortugueseBrazil(),
            [FallbackLocale] = BuildEnglish(),
        });

    /// <summary>Locale → (key → text).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> All => AllLazy.Value;

    private static IReadOnlyDictionary<string, string> BuildPortugueseBrazil() => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // ── Necessidades ────────────────────────────────────────────────────────────────
        ["need.energy.name"] = "Energia",
        ["need.energy.short"] = "ENE",
        ["need.energy.desc"] = "Descanso e sono. Cai todo dia e é o que mais pesa numa semana cheia.",
        ["need.nutrition.name"] = "Nutrição",
        ["need.nutrition.short"] = "NUT",
        ["need.nutrition.desc"] = "Qualidade da alimentação. Comer mal derruba o preparo físico.",
        ["need.hygiene.name"] = "Higiene",
        ["need.hygiene.short"] = "HIG",
        ["need.hygiene.desc"] = "Cuidado pessoal. Barato de resolver e fácil de deixar pra depois.",
        ["need.fitness.name"] = "Preparo Físico",
        ["need.fitness.short"] = "FIS",
        ["need.fitness.desc"] = "Condicionamento. O eixo da vida de um atleta.",
        ["need.muscle_condition.name"] = "Condição Muscular",
        ["need.muscle_condition.short"] = "MUS",
        ["need.muscle_condition.desc"] = "O oposto da dor muscular. Dá pra estar descansado e ainda assim dolorido — e é a dor, não o cansaço, que vira lesão.",
        ["need.morale.name"] = "Moral",
        ["need.morale.short"] = "MOR",
        ["need.morale.desc"] = "Estado mental. Alimenta a forma e a pressão de eventos.",
        ["need.social.name"] = "Social",
        ["need.social.short"] = "SOC",
        ["need.social.desc"] = "Companhia e relacionamentos. Isolamento derruba a moral.",
        ["need.focus.name"] = "Foco",
        ["need.focus.short"] = "FOC",
        ["need.focus.desc"] = "Clareza mental. O eixo da vida de um treinador.",

        // ── Faixas ──────────────────────────────────────────────────────────────────────
        ["band.critical"] = "Crítico",
        ["band.low"] = "Baixo",
        ["band.adequate"] = "Adequado",
        ["band.good"] = "Ótimo",

        // ── Carreira ────────────────────────────────────────────────────────────────────
        ["role.player"] = "Jogador",
        ["role.manager"] = "Treinador",

        // ── Atividades ──────────────────────────────────────────────────────────────────
        ["activity.sleep.name"] = "Dormir",
        ["activity.sleep.desc"] = "Uma noite inteira. Recupera energia, foco e um pouco da musculatura.",
        ["activity.shower.name"] = "Banho",
        ["activity.shower.desc"] = "Meia hora que resolve a higiene do dia.",
        ["activity.meal.name"] = "Refeição Adequada",
        ["activity.meal.desc"] = "Comer direito, sentado, sem pressa.",
        ["activity.physio.name"] = "Fisioterapia",
        ["activity.physio.desc"] = "Sessão no departamento médico. A forma mais rápida de tirar a dor muscular.",
        ["activity.socialise.name"] = "Ver os Amigos",
        ["activity.socialise.desc"] = "Sair um pouco. Custa energia, devolve moral.",
        ["activity.family_time.name"] = "Tempo com a Família",
        ["activity.family_time.desc"] = "O maior ganho de moral disponível, e sai de graça.",
        ["activity.leisure.name"] = "Lazer",
        ["activity.leisure.desc"] = "Gastar um pouco pra desligar de verdade.",
        ["activity.media_duty.name"] = "Compromisso de Mídia",
        ["activity.media_duty.desc"] = "Obrigação contratual. Custa em todos os eixos e não dá pra fugir.",
        ["activity.individual_training.name"] = "Treino Individual",
        ["activity.individual_training.desc"] = "Compra preparo físico com descanso, comida e músculo dolorido.",
        ["activity.gym_session.name"] = "Academia",
        ["activity.gym_session.desc"] = "Versão mais curta e mais barata do treino individual.",
        ["activity.film_study.name"] = "Estudo de Vídeo",
        ["activity.film_study.desc"] = "Analisar o adversário. O maior ganho de foco do treinador.",
        ["activity.staff_meeting.name"] = "Reunião com a Comissão",
        ["activity.staff_meeting.desc"] = "Alinhar a semana. Ganha foco e entrosamento.",
        ["activity.scouting_trip.name"] = "Viagem de Observação",
        ["activity.scouting_trip.desc"] = "Um dia inteiro fora. Caro em dinheiro, energia e convívio.",

        // ── Mundo de testes ─────────────────────────────────────────────────────────────
        ["location.test.verith.name"] = "Verith",
        ["location.test.verith.desc"] = "Cidade de testes. Será substituída pelo mundo real do City Searcher.",
        ["location.test.verith#centro.name"] = "Centro",
        ["location.test.verith#centro.desc"] = "Onde se mora, come e resolve a vida.",
        ["location.test.verith#orla.name"] = "Orla",
        ["location.test.verith#orla.desc"] = "Vida noturna e fim de tarde.",
        ["location.test.verith#distrito-esportivo.name"] = "Distrito Esportivo",
        ["location.test.verith#distrito-esportivo.desc"] = "Estádio, CT e tudo que é trabalho.",
        ["location.test.verith#apartamento.name"] = "Apartamento",
        ["location.test.verith#apartamento.desc"] = "Sua casa. Dormir, tomar banho, ficar com a família.",
        ["location.test.verith#centro-de-treinamento.name"] = "Centro de Treinamento",
        ["location.test.verith#centro-de-treinamento.desc"] = "Onde o trabalho de verdade acontece.",
        ["location.test.verith#estadio.name"] = "Estádio",
        ["location.test.verith#estadio.desc"] = "Dia de jogo.",
        ["location.test.verith#academia.name"] = "Academia",
        ["location.test.verith#academia.desc"] = "Complemento de força e condicionamento.",
        ["location.test.verith#departamento-medico.name"] = "Departamento Médico",
        ["location.test.verith#departamento-medico.desc"] = "Fisioterapia e recuperação.",
        ["location.test.verith#restaurante.name"] = "Restaurante",
        ["location.test.verith#restaurante.desc"] = "Comida de verdade, longe da marmita.",
        ["location.test.verith#bar-da-orla.name"] = "Bar da Orla",
        ["location.test.verith#bar-da-orla.desc"] = "Onde se encontra todo mundo.",
        ["location.test.verith#galeria.name"] = "Galeria",
        ["location.test.verith#galeria.desc"] = "Compras e lazer.",
        ["location.test.verith#centro-de-midia.name"] = "Centro de Mídia",
        ["location.test.verith#centro-de-midia.desc"] = "Entrevistas, coletivas e ensaios de patrocinador.",

        // ── Tipos de local ──────────────────────────────────────────────────────────────
        ["location.kind.region"] = "Região",
        ["location.kind.state"] = "Estado",
        ["location.kind.subregion"] = "Sub-região",
        ["location.kind.city"] = "Cidade",
        ["location.kind.district"] = "Bairro",
        ["location.kind.venue"] = "Local",

        // ── Eventos ─────────────────────────────────────────────────────────────────────
        ["event.tier.high"] = "ALTO RISCO — isso vai te seguir",
        ["event.tier.medium"] = "DECISÃO",
        ["event.tier.low"] = "AVISO",
        ["event.acknowledge"] = "Entendido",
        ["event.no_consequence"] = "Sem consequência direta.",

        ["event.contract_offer.title"] = "Proposta de Contrato",
        ["event.contract_offer.prompt"] = "Seu empresário tem uma proposta na mesa. Como você quer jogar isso?",
        ["event.contract_offer.choice.sign.label"] = "Assinar agora",
        ["event.contract_offer.choice.sign.desc"] = "Segurança hoje, poder de barganha perdido amanhã.",
        ["event.contract_offer.choice.hold.label"] = "Segurar por mais",
        ["event.contract_offer.choice.hold.desc"] = "Apostar na sua forma. O vestiário vai perceber de um jeito ou de outro.",
        ["event.contract_offer.choice.walk.label"] = "Recusar de vez",
        ["event.contract_offer.choice.walk.desc"] = "Só quem confia demais em si mesmo queima uma ponte tão cedo.",

        ["event.press_conference.title"] = "Coletiva de Imprensa",
        ["event.press_conference.prompt"] = "Um repórter pergunta sobre os boatos no vestiário.",
        ["event.press_conference.choice.deflect.label"] = "Desviar da pergunta",
        ["event.press_conference.choice.deflect.desc"] = "Seguro, esquecível, e ninguém sai chateado.",
        ["event.press_conference.choice.back_squad.label"] = "Defender o elenco publicamente",
        ["event.press_conference.choice.back_squad.desc"] = "Não custa nada além da manchete.",
        ["event.press_conference.choice.hit_back.label"] = "Peitar o repórter",
        ["event.press_conference.choice.hit_back.desc"] = "Ótima matéria. O treinador vai ter visto.",

        ["event.flight_delay.title"] = "Voo Atrasado",
        ["event.flight_delay.prompt"] = "Horas perdidas no aeroporto.",

        // ── Interface ───────────────────────────────────────────────────────────────────
        ["hud.tile.wellbeing"] = "bem-estar",
        ["hud.tile.form"] = "forma",
        ["hud.tile.date"] = "data",
        ["hud.tile.career"] = "carreira",
        ["hud.critical"] = "CRÍTICO",
        ["panel.wellbeing"] = "BEM-ESTAR",
        ["panel.actions"] = "AÇÕES",
        ["panel.activity.no_change"] = "sem mudança — essas necessidades já estavam cheias.",
        ["activity.cost"] = "Custo",
        ["activity.duration"] = "Duração",

        // ── Celular ─────────────────────────────────────────────────────────────────────
        ["phone.title"] = "Celular",
        ["phone.app.health"] = "Saúde",
        ["phone.app.agenda"] = "Agenda",
        ["phone.app.map"] = "Mapa",
        ["phone.travel_time"] = "min de deslocamento",
        ["phone.current_location"] = "Você está aqui",

        // ── Dicas de botão ──────────────────────────────────────────────────────────────
        ["prompt.select"] = "Selecionar",
        ["prompt.back"] = "Voltar",
        ["prompt.close"] = "Fechar",
        ["prompt.menu"] = "Menu",
        ["prompt.phone"] = "Celular",
        ["prompt.travel"] = "Ir para",

        ["phone.app.bank"] = "Banco",
        ["bank.balance"] = "Saldo atual",
        ["bank.weekly_wage"] = "Salário semanal",

        // ── Menu rápido ─────────────────────────────────────────────────────────────────
        ["menu.title"] = "Menu",
        ["menu.career_role"] = "Carreira atual",
        ["menu.role_hint"] = "Trocar de carreira mantém suas necessidades exatamente onde estão — só muda o peso de cada uma. Você continua sendo a mesma pessoa que dormiu mal ontem.",
        ["menu.resume"] = "Continuar",
        ["menu.main.title"] = "Soccer Dream Game",
        ["menu.main.life_sim"] = "Viver o Dia a Dia",
        ["menu.main.play_fixture"] = "Jogar a Próxima Partida",
        ["menu.main.advance_calendar"] = "Avançar o Calendário",
        ["menu.main.no_fixture"] = "Nenhuma partida pendente para o seu clube.",
        ["activity.unaffordable"] = "Saldo insuficiente",

        // ── Valores derivados ───────────────────────────────────────────────────────────
        ["derived.injury_risk"] = "Risco de Lesão",
        ["derived.stress"] = "Estresse",
        ["derived.decision_quality"] = "Qualidade de Decisão",
        ["derived.index"] = "Índice Geral",
    };

    private static IReadOnlyDictionary<string, string> BuildEnglish() => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["need.energy.name"] = "Energy",
        ["need.energy.short"] = "ENE",
        ["need.energy.desc"] = "Rest and sleep. Drains daily and dominates a packed week.",
        ["need.nutrition.name"] = "Nutrition",
        ["need.nutrition.short"] = "NUT",
        ["need.nutrition.desc"] = "Diet quality. Eating badly costs conditioning.",
        ["need.hygiene.name"] = "Hygiene",
        ["need.hygiene.short"] = "HYG",
        ["need.hygiene.desc"] = "Personal upkeep. Cheap to fix and easy to postpone.",
        ["need.fitness.name"] = "Fitness",
        ["need.fitness.short"] = "FIT",
        ["need.fitness.desc"] = "Conditioning. The axis of an athlete's life.",
        ["need.muscle_condition.name"] = "Muscle Condition",
        ["need.muscle_condition.short"] = "MUS",
        ["need.muscle_condition.desc"] = "The opposite of soreness. You can be rested and still sore — and it is soreness, not tiredness, that becomes an injury.",
        ["need.morale.name"] = "Morale",
        ["need.morale.short"] = "MOR",
        ["need.morale.desc"] = "Mental state. Feeds form and event pressure.",
        ["need.social.name"] = "Social",
        ["need.social.short"] = "SOC",
        ["need.social.desc"] = "Company and relationships. Isolation costs morale.",
        ["need.focus.name"] = "Focus",
        ["need.focus.short"] = "FOC",
        ["need.focus.desc"] = "Mental sharpness. The axis of a manager's life.",

        ["band.critical"] = "Critical",
        ["band.low"] = "Low",
        ["band.adequate"] = "Adequate",
        ["band.good"] = "Good",

        ["role.player"] = "Player",
        ["role.manager"] = "Manager",

        ["activity.sleep.name"] = "Sleep",
        ["activity.sleep.desc"] = "A full night. Restores energy, focus and some muscle freshness.",
        ["activity.shower.name"] = "Shower",
        ["activity.shower.desc"] = "Half an hour that settles the day's hygiene.",
        ["activity.meal.name"] = "Proper Meal",
        ["activity.meal.desc"] = "Eating properly, sitting down, unhurried.",
        ["activity.physio.name"] = "Physio Session",
        ["activity.physio.desc"] = "The fastest way to clear muscle soreness.",
        ["activity.socialise.name"] = "See Friends",
        ["activity.socialise.desc"] = "Get out for a bit. Costs energy, returns morale.",
        ["activity.family_time.name"] = "Family Time",
        ["activity.family_time.desc"] = "The biggest morale gain available, and it is free.",
        ["activity.leisure.name"] = "Leisure",
        ["activity.leisure.desc"] = "Spend a little to properly switch off.",
        ["activity.media_duty.name"] = "Media Duty",
        ["activity.media_duty.desc"] = "Contractual obligation. Costs on every axis and cannot be skipped.",
        ["activity.individual_training.name"] = "Individual Training",
        ["activity.individual_training.desc"] = "Buys conditioning with rest, food and sore muscles.",
        ["activity.gym_session.name"] = "Gym Session",
        ["activity.gym_session.desc"] = "A shorter, cheaper version of individual training.",
        ["activity.film_study.name"] = "Opposition Film Study",
        ["activity.film_study.desc"] = "The manager's biggest focus gain.",
        ["activity.staff_meeting.name"] = "Staff Meeting",
        ["activity.staff_meeting.desc"] = "Align the week. Gains focus and cohesion.",
        ["activity.scouting_trip.name"] = "Scouting Trip",
        ["activity.scouting_trip.desc"] = "A full day away. Expensive in money, energy and company.",

        ["location.test.verith.name"] = "Verith",
        ["location.test.verith.desc"] = "Test city. To be replaced by the real City Searcher world.",
        ["location.test.verith#centro.name"] = "Downtown",
        ["location.test.verith#centro.desc"] = "Where you live, eat and sort your life out.",
        ["location.test.verith#orla.name"] = "Waterfront",
        ["location.test.verith#orla.desc"] = "Nightlife and late afternoons.",
        ["location.test.verith#distrito-esportivo.name"] = "Sports District",
        ["location.test.verith#distrito-esportivo.desc"] = "Stadium, training ground, and everything that is work.",
        ["location.test.verith#apartamento.name"] = "Apartment",
        ["location.test.verith#apartamento.desc"] = "Home. Sleep, shower, family time.",
        ["location.test.verith#centro-de-treinamento.name"] = "Training Ground",
        ["location.test.verith#centro-de-treinamento.desc"] = "Where the real work happens.",
        ["location.test.verith#estadio.name"] = "Stadium",
        ["location.test.verith#estadio.desc"] = "Matchday.",
        ["location.test.verith#academia.name"] = "Gym",
        ["location.test.verith#academia.desc"] = "Strength and conditioning top-up.",
        ["location.test.verith#departamento-medico.name"] = "Medical Department",
        ["location.test.verith#departamento-medico.desc"] = "Physio and recovery.",
        ["location.test.verith#restaurante.name"] = "Restaurant",
        ["location.test.verith#restaurante.desc"] = "Real food, away from the lunchbox.",
        ["location.test.verith#bar-da-orla.name"] = "Waterfront Bar",
        ["location.test.verith#bar-da-orla.desc"] = "Where everyone turns up.",
        ["location.test.verith#galeria.name"] = "Arcade",
        ["location.test.verith#galeria.desc"] = "Shopping and leisure.",
        ["location.test.verith#centro-de-midia.name"] = "Media Centre",
        ["location.test.verith#centro-de-midia.desc"] = "Interviews, press conferences and sponsor shoots.",

        ["location.kind.region"] = "Region",
        ["location.kind.state"] = "State",
        ["location.kind.subregion"] = "Subregion",
        ["location.kind.city"] = "City",
        ["location.kind.district"] = "District",
        ["location.kind.venue"] = "Venue",

        ["event.tier.high"] = "HIGH STAKES — this will follow you",
        ["event.tier.medium"] = "DECISION",
        ["event.tier.low"] = "NOTICE",
        ["event.acknowledge"] = "Acknowledge",
        ["event.no_consequence"] = "No direct consequence.",

        ["event.contract_offer.title"] = "Contract Offer",
        ["event.contract_offer.prompt"] = "Your agent has an offer on the table. How do you want to play it?",
        ["event.contract_offer.choice.sign.label"] = "Sign now",
        ["event.contract_offer.choice.sign.desc"] = "Security today, leverage gone tomorrow.",
        ["event.contract_offer.choice.hold.label"] = "Hold out for more",
        ["event.contract_offer.choice.hold.desc"] = "Bet on your form. The dressing room will notice either way.",
        ["event.contract_offer.choice.walk.label"] = "Walk away",
        ["event.contract_offer.choice.walk.desc"] = "Only a player who backs himself burns a bridge this early.",

        ["event.press_conference.title"] = "Press Conference",
        ["event.press_conference.prompt"] = "A reporter asks about the dressing-room rumours.",
        ["event.press_conference.choice.deflect.label"] = "Deflect the question",
        ["event.press_conference.choice.deflect.desc"] = "Safe, forgettable, and nobody is upset.",
        ["event.press_conference.choice.back_squad.label"] = "Back your teammates publicly",
        ["event.press_conference.choice.back_squad.desc"] = "Costs you nothing but the headline.",
        ["event.press_conference.choice.hit_back.label"] = "Hit back at the reporter",
        ["event.press_conference.choice.hit_back.desc"] = "Great copy. The manager will have seen it.",

        ["event.flight_delay.title"] = "Flight Delay",
        ["event.flight_delay.prompt"] = "Hours lost at the airport.",

        ["hud.tile.wellbeing"] = "wellbeing",
        ["hud.tile.form"] = "form",
        ["hud.tile.date"] = "date",
        ["hud.tile.career"] = "career",
        ["hud.critical"] = "CRITICAL",
        ["panel.wellbeing"] = "WELLBEING",
        ["panel.actions"] = "ACTIONS",
        ["panel.activity.no_change"] = "no change — those needs were already full.",
        ["activity.cost"] = "Cost",
        ["activity.duration"] = "Duration",

        ["phone.title"] = "Phone",
        ["phone.app.health"] = "Health",
        ["phone.app.agenda"] = "Agenda",
        ["phone.app.map"] = "Map",
        ["phone.travel_time"] = "min to travel",
        ["phone.current_location"] = "You are here",

        ["prompt.select"] = "Select",
        ["prompt.back"] = "Back",
        ["prompt.close"] = "Close",
        ["prompt.menu"] = "Menu",
        ["prompt.phone"] = "Phone",
        ["prompt.travel"] = "Travel to",

        ["phone.app.bank"] = "Bank",
        ["bank.balance"] = "Current balance",
        ["bank.weekly_wage"] = "Weekly wage",

        ["menu.title"] = "Menu",
        ["menu.career_role"] = "Current career",
        ["menu.role_hint"] = "Switching career keeps your needs exactly where they are — only their weighting changes. You are still the person who slept badly last night.",
        ["menu.resume"] = "Resume",
        ["menu.main.title"] = "Soccer Dream Game",
        ["menu.main.life_sim"] = "Live the Day",
        ["menu.main.play_fixture"] = "Play Next Fixture",
        ["menu.main.advance_calendar"] = "Advance Calendar",
        ["menu.main.no_fixture"] = "No unplayed fixture for your club.",
        ["activity.unaffordable"] = "Insufficient balance",

        ["derived.injury_risk"] = "Injury Risk",
        ["derived.stress"] = "Stress",
        ["derived.decision_quality"] = "Decision Quality",
        ["derived.index"] = "Overall Index",
    };
}
