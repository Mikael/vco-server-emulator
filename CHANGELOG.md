# Changelog

## 2026-10-17

### New game systems
Every client command that used to answer "not available" now has a server side, built from the client's own windows,
messages and data tables. Rules the original server kept to itself (costs, chances, schedules, rewards) are guesses and
live in the matching `data\game\*.json` file so they can be tuned.

- **Auction house and pawn shop** - hourly auctions in the Sailor Bar, prizes and pawning at the Warehouse Managers.
- **Fire flags and belief merging** - forge, promote and reform flags; faith fusion at the Athens Blacksmith.
- **Star souls** - charts, eight soul slots, upgrade, rebuild, advance and disassemble.
- **Imprints (totems)** - appraise, advance, dismantle, skills, remodel, potential points, charge and release.
- **Equipment** - upgrade, evolution, stars, star saving, consolidation, item memory, suit exchange, recast, recast
  records, attribute transfer and nobility gear tempering.
- **Aides** - recruiting, rest room and posts, training, transfer, suits and skills.
- **Role bank, parcel postage, booth search, viewing other players' gear, leave words.**
- **Guilds** - sieges and bids, city statues, colonies and their development routes, Merchant Alliances, guild
  instances (Secret Isle), guild matches and betting, medals, construction and activity.
- **Battles and instances** - battle queue and entry, battle reports, instance queue, the Golden Tower, match records,
  team recruiting and the battle vote-kick.
- **Pets and sea** - pet whistles and pet skills, sea mines, auto-sail, robbery, contraband, ship model merge and the
  ship's inner rebuild.
- **Sailor bar and leisure** - quiz, dragon-boat race, link game, football and Olympic guessing, treasure boxes, camps
  and popularity, military rank, exploit medals, model change and appearance options.

### Quests
- 1,261 quests load (was 1,585, most without a working NPC). Item, profession-EXP and skill rewards, choose-one rewards
  at turn-in, multi-target objectives, chains and daily quests.
- New characters start with "The Birth of New Hero" and the storyline follows it.
- The client's quest talks (offer, accept and finish) play, with the right Voyage Log groups.
- The Athens Tavern does not exist in this client; its NPCs stand at the tavern door so quests 1003 and 1004 work.

### Fixes from play testing
- NPC portraits show in the talk window; Goodbye closes it.
- Blue door markers for every scene exit; 15 missing exits added and arrival points moved onto walkable ground.
- Combat: the player runs up to the target and only hits within weapon reach, at the weapon's attack speed.
  Monsters face their target and only hit in range.
- Online rewards keep counting after a claim and are saved over logins.
- The profession title shows under the name (turn on `TopWorkLevel` in the client's game settings).
- Character creation offers four figures (two men, two women); the second woman uses body 3402006 because 3402002
  renders without a head. Characters saved with the wrong body are repaired at login.

### World and items
- Gear icons, worn models, suits (for example Ba Yuan Armour turns into body 4005428/4005429), weapon models and
  glow, bows, stats and military-rank requirements.
- Monster models, sizes, attack and death animations; monsters lie down before they disappear.
- Golden light pillar on level-up and quest completion.
- NPC models picked from the NPC's role where the client has no model for it.

### Tools
- Test commands `/item` and `/honor`.
