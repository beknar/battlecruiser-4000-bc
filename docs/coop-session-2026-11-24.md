# The co-op session of 24–25 November 2026

Compiled from the co-op server's comms log (`chatLog`) with `Tools/CoopAI/chat_dataset.py
--anonymise`. The players' names are replaced by "Player 1", and the AI wingman keeps its own.

## The session
- **When:** 24 Nov 23:59 to 25 Nov 00:28 UTC, about half an hour, in the Nine Hells.
- **Who:** one player, on the web game (WebGPU, Chrome), with the AI wingman on the server.
- **How:** every order was spoken (via `voice`). The browser recognised the speech and the game sent
  it as chat.
- **Who answered:** the bot answered orders itself (`order`) and said what it was doing (`event`).
  Claude answered the rest as the wingman through the bot's API (`llm`).
- **Totals:** 74 messages, 27 from the player and 47 from the wingman (the first is its greeting, two hours earlier), and 31 answers paired with
  what they answer (`pairs.jsonl`).

## What it shows

### Orders the wingman can't carry out yet
| Asked | How often | What it should do |
| --- | --- | --- |
| "salvage", "scavenge" | 3 (5 tries) | salvage the wrecks round it (PR 7) |
| "attack my target" | 3 | attack what the player has locked (PR 4) |
| "defend me", "I'm under attack" | 2 | come to the player from wherever it is and fight what's on them (PR 4) |
| "where are you" | 1 | say where it is and how far from the player (PR 4) |
| "attack", sent in warp | 1 | go to its leader's fight, not answer "no enemies in sight" (PR 4) |
| a target's name alone ("Fang Hunter") | 1 | attack that target (PR 4) |

### What the speech recogniser misheard
| Said (most likely) | Heard |
| --- | --- |
| Wingman | "women", "amen", "only man", "hey man" |
| sell ore | "cellar", "cell or", "sell the or" |
| mining | "morning" |
| attack | "pack" |
| Fang Hunter | "Frank Hunter" |

- **Why the name became a problem:** when no wingman is named, the game puts the name first, so
  "women mine" went out as "Wingman, women mine". The bot couldn't read it as an order and left it
  to the LLM. The LLM guessed it right, but only because one was listening. PR 8 fixes that.
- **What worked:** status (asked 4 times), mine, give credits (it asked "how many?", then "all"
  went through), and following.
- **The wingman's earnings:** it sold three loads of 599 t at Heaven Highport, for 40,638 cr,
  154,926 cr and 25,234 cr, and gave the player the first 40,638 cr.

## The conversation
"via" is how each message was made:
- **The player's messages:** `voice` (spoken) or `typed`.
- **The wingman's messages:**
  - `order`: an order it carried out.
  - `event`: something it reports by itself.
  - `llm`: Claude's answer through the bot's API.

| Time (UTC) | Who | Via | Message |
| --- | --- | --- | --- |
| 21:55:37 | Wingman | event | Wingman on station. Orders by name: "Wingman, follow me / hold / attack / warp to <place> / dock / mine / sell / cargo / give credits / status". |
| 23:59:52 | Wingman | event | Wingman: welcome, Player 1. Forming up on you; orders by name ("Wingman, help"). Comms are logged on this server. |
| 23:59:52 | Wingman | event | Wingman: Player 1 has landed on K'rzyn. Holding in high orbit over it. |
| 00:00:25 | Player 1 | voice | Wingman, only man mine |
| 00:00:31 | Player 1 | voice | Wingman, women mine |
| 00:00:39 | Wingman | order | Wingman: mining the nearest asteroid field; I'll sell the ore when the hold is full. |
| 00:00:40 | Wingman | llm | Wingman: read you as "go and mine", Player 1. Heading for the nearest field; your mic's coming through a bit scrambled. |
| 00:01:04 | Player 1 | voice | Wingman, status |
| 00:01:04 | Wingman | order | Wingman: Gun frigate Frigate, hull 100 %, shield 100 %, hold 0/600 t, 0 cr; flying to M-type asteroid (metallic) (2.7 km off), near Inner Belt. |
| 00:01:10 | Wingman | event | Wingman: engaging Claim-jumper Drone B. |
| 00:02:39 | Player 1 | voice | Wingman, amen scavenge |
| 00:02:46 | Player 1 | voice | Wingman, scavenge |
| 00:02:47 | Wingman | event | Wingman: back to mining. |
| 00:02:54 | Wingman | llm | Wingman: say again, Player 1, that came through as "amen scavenge". The Scavengers here are already scrap, and I can't salvage wrecks, so I'm staying on the rocks at Inner Belt. |
| 00:03:00 | Wingman | llm | Wingman: can't scavenge, Player 1, I've no salvage gear rigged. I can mine, sell, fight or follow you. Staying on the rocks unless you say otherwise. |
| 00:03:32 | Player 1 | voice | Wingman, women mine |
| 00:03:44 | Wingman | llm | Wingman: already mining, Player 1. Closing on a metallic rock at Inner Belt, 600 m out, hold empty for now. |
| 00:05:30 | Player 1 | voice | Wingman, women status |
| 00:05:35 | Player 1 | voice | Wingman, status |
| 00:05:35 | Wingman | order | Wingman: Gun frigate Frigate, hull 100 %, shield 100 %, hold 599/600 t, 0 cr; in warp, near Abaddon. |
| 00:05:42 | Wingman | llm | Wingman: hull and shields full, hold full with about 600 t of ore, no credits yet. In warp past Abaddon, taking the load to market to sell. |
| 00:07:13 | Wingman | event | Wingman: sold 599 t of ore at Heaven Highport for 40,638 credits. |
| 00:07:28 | Player 1 | voice | Wingman, give me credits |
| 00:07:28 | Wingman | order | Wingman: how many credits? I've 40,638 cr ("give me 500 credits", or "give me all your credits"). |
| 00:07:40 | Player 1 | voice | Wingman, give me all your credits |
| 00:07:40 | Wingman | order | Wingman: transferred 40,638 cr to Player 1; 0 cr left. |
| 00:08:35 | Player 1 | voice | Wingman mine |
| 00:08:35 | Wingman | order | Wingman: mining the nearest asteroid field; I'll sell the ore when the hold is full. |
| 00:11:48 | Wingman | event | Wingman: sold 599 t of ore at Heaven Highport for 154,926 credits. |
| 00:12:11 | Wingman | event | Wingman: engaging Claim-jumper Drone A. |
| 00:12:33 | Wingman | event | Wingman: back to mining. |
| 00:13:12 | Wingman | event | Wingman: engaging Scavenger A. |
| 00:13:33 | Player 1 | voice | Wingman, hey man I'm under attack |
| 00:13:46 | Wingman | order | Wingman: following Player 1. |
| 00:13:47 | Player 1 | voice | Wingman, defend me |
| 00:13:49 | Wingman | llm | Wingman: on my way, Player 1. Breaking off at Inner Belt and warping to you. Hang on. |
| 00:14:05 | Wingman | llm | Wingman: in warp to you now, about 200,000 km out. I'll take anything on you when I drop in. |
| 00:14:07 | Wingman | event | Wingman: engaging Vrask Fang Hunter. |
| 00:14:19 | Player 1 | voice | Wingman, attack my target |
| 00:14:19 | Wingman | order | Wingman: no target called my target in sight. |
| 00:14:25 | Player 1 | voice | Wingman, where are you |
| 00:14:33 | Wingman | llm | Wingman: can't see your lock, Player 1. I'm on the Fang Hunter. Skink's at 44 %, say "attack the Skink" if that's yours. |
| 00:14:45 | Wingman | llm | Wingman: right behind you, 1.7 km off at Havilah. Fang Hunter's down to 67 % and I'm still on it. |
| 00:15:10 | Player 1 | voice | Wingman, status |
| 00:15:10 | Wingman | order | Wingman: Gun frigate Frigate, hull 100 %, shield 100 %, hold 0/600 t, 154,926 cr; closing on Player 1 (1.7 km), near Havilah. |
| 00:16:01 | Player 1 | voice | Wingman, cellar |
| 00:16:06 | Player 1 | voice | Wingman, cell or |
| 00:16:12 | Player 1 | voice | Wingman, sell the or |
| 00:16:12 | Wingman | order | Wingman: no ore aboard to sell. |
| 00:16:12 | Wingman | llm | Wingman: reading that as "sell ore". Nothing to sell, Player 1, the hold's empty: I sold the last load at Heaven Highport. Both Vrask are scrap. Holding on you with 154,926 cr aboard. |
| 00:16:24 | Wingman | llm | Wingman: hold's empty, Player 1, no ore to sell. Say "mine" and I'll go fill it. |
| 00:19:27 | Player 1 | voice | Wingman, morning |
| 00:19:38 | Player 1 | voice | Wingman, hey man mine |
| 00:19:41 | Wingman | order | Wingman: mining the nearest asteroid field; I'll sell the ore when the hold is full. |
| 00:19:41 | Wingman | llm | Wingman: took that as "mining", Player 1. Off to fill the hold; call if you need me. |
| 00:19:47 | Wingman | llm | Wingman: already on it, Player 1, heading for the nearest field. |
| 00:22:19 | Wingman | event | Wingman: sold 599 t of ore at Heaven Highport for 25,234 credits. |
| 00:22:42 | Wingman | event | Wingman: engaging Claim-jumper Drone C. |
| 00:22:56 | Wingman | event | Wingman: back to mining. |
| 00:23:41 | Wingman | event | Wingman: engaging Scavenger A. |
| 00:26:25 | Player 1 | voice | Wingman, pack my target |
| 00:26:30 | Player 1 | voice | Wingman, attack my target |
| 00:26:30 | Wingman | order | Wingman: no target called my target in sight. |
| 00:26:38 | Player 1 | voice | Wingman, attack us I'll forget |
| 00:26:38 | Wingman | order | Wingman: no target called us I'll forget in sight. |
| 00:26:40 | Wingman | order | Wingman: following Player 1. |
| 00:26:40 | Wingman | llm | Wingman: can't see your lock from here, Player 1, you're 2 million km off. Warping to you now; I'll hit whatever's on you, or name it and I'll take that. |
| 00:26:45 | Player 1 | voice | Wingman, attack |
| 00:26:45 | Wingman | order | Wingman: no enemies in sight. |
| 00:26:46 | Player 1 | voice | Wingman, Frank Hunter |
| 00:27:00 | Wingman | event | Wingman: engaging Vrask Fang Hunter. |
| 00:27:11 | Wingman | llm | Wingman: on the Fang Hunter, Player 1. It's at 91 %, 2 km off you. |
| 00:27:36 | Player 1 | voice | Wingman, Salvage |
| 00:27:43 | Wingman | llm | Wingman: no salvage gear on this frigate, Player 1, so the wrecks are yours. I'll cover you while you work them. |
