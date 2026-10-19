# Changelog

## 2026-10-19

### Character and equipment
- **Item mall purchases ask first**: after picking the amount, the client's own confirmation box opens ("Please confirm. It costs [100point] ... Are you sure you want to buy it?"). Points are only taken when you press Yes, and the chat then shows the client's "You purchased [item] from the Item Mall" line.
- **Gestures work** (Command Interface > Gesture: wave, blow kiss, salute, nod, pray...). The client only has gesture animations for empty hands, so your weapon is put away for the gesture and comes back afterwards.
- **Gathering tools are worn, not used up**: pickaxes, felling axes, hoes, hammers and fishing nets now go to the equipment tab and are held in the weapon hand (pickaxe, felling axe and hoe show in the hand). Using one from the item tab equips it; tools already sitting in the item tab move to the equipment tab when you log in.
- **Equip sounds match the item**: weapons play the client's own sword / falchion / axe / gun sounds (ini\window\viewsound.ini) instead of the ship-equipment sound; clothes keep the clothes sound.
- **[Gain] messages for items**: items you mine, fell, buy, make, loot or get from the mall show as a [Gain] line with the pickup sound, like the original.
- **Item mall Clothes tab**: 20 fashion suits (Han Minority, Gandu Battle Armor, Spirit Clothes, Grim Reaper, Ming Minority, Zhongsan, Youth Clothes, Atlantis, Tiger Suit, the rabbit dresses, the evening dresses...). They and their Improved/Refined versions now wear the client's own fashion models and icons (ini\ModelInfos.ini change-model list) instead of the battle uniform.
- **Smelting the mined ores**: Iron Ore, Copper Ore and Violet Gold Ore can now be smelted (into Pure Iron, Copper and Purple Gold), and a finished craft that does not fit in your bag says so instead of disappearing.
- **Planting**: Farmers sell seeds (Linen, Jute, Rosemary, Catnip, Sisal, Affine Cudweed, Cotton, Thyme, Coffee, Graceful Jessamine, Chamomile, Notoginseng, Spun Gold, Ramie, American Ginseng, Reishi) and the hoe. With the hoe in hand, use a seed on the farm around the Farmer (Athens outskirts, and the camp on Cyprus Island): the plant grows for a few minutes, then click it (or use Harvesting to take several at once) for the crop, sometimes with a Normal / High-level / Quality Crop for alchemy. Higher crops need more Planting and Botany Study. The cloth and potion recipes that use these crops (Jute, Cotton, Spun Gold, Ramie cloth; Catnip, Jessamine, Notoginseng potions) can now be made, the Low-level Healing Potion takes Affine Cudweed, and crafting also takes trade goods (Linen, Wool, Ginseng...) from your ship's hold.
- **Fishing**: with a fishing net equipped and Fishing learned, your crew hauls in fish while you sail (Fish, Shrimp, Salmon, Lobster, Australian lobster by Fishing level, more with the High-level net), at the cost of energy.
- **Cannons can be taken off**: right-click a cannon slot in the ship window to put those cannons into your bag; right-click cannon items in the bag to mount them again (one type per ship, empty gun ports first).
- **Mining and timber-felling**: the suburbs have ore points (at the rocks) and timber points (by the trees). With the pickaxe or felling axe equipped, click one: you walk there, gather for a few seconds and get ore (Ore, Iron Ore, Impure Copper Ore...) or logs (Level 1-10 log) for Mining/Timber-felling experience. Ore Study / Log Study decide the highest grade you can take; a point is used up for a while after someone takes from it.

### Ships and sea
- **Sea instances have enemies**: English Channel (Spanish Armada with Medina-Sidonia's Flagship), Crete Pirates (Iron Hand Edward), Gibraltar (Steel Tooth / Golden Eye / Jackal armed ships) and North Sea Pirates (Nordic Pirate Admiral Carl Emanuel's Zeus), placed at the boss and fleet marks of the client maps.
- **Sea instances start on the water** at the instance exit instead of on land, and sailing into the exit ring takes you back out.
- **Battle Info button** shows in sea instances: credits, difficulty and the enemy ships left in each quarter of the map.
- **Sea instance report**: sinking the last enemy ship opens the client's report with every player's ships sunk and credits, and who sank the boss.
- **Ocean battles**: the high sea has no broadsides in the client, so attacking an NPC fleet there now takes you into a sea battle field (the open-sea scene 1900001) with the fleet's flagship and its escorts, fought in real time with cannons. Sink them all to win and you are back on the open sea where the fight started; sail far enough away from them to escape; get sunk and you are towed to port as before. Team mates sailing close by join the battle with you.
- **Ship against ship**: Force Attack at Sea on another player's ship fires broadsides at it in coastal waters and on battle fields; on the high sea both sides (with their team mates close by) are taken to a battle field and fight it out, and the side left afloat wins. A ship that was attacked can fire back with plain clicks.
- **World map at sea**: the explored-area fog of the ocean maps clears as you sail and is remembered; team mates show on the map; storms show as storm warnings.
- **Storms and floating goods on the high sea**: storms drift across the oceans and tear at the sails and hull of any ship inside; drifting goods can be fished out of the water for silver or cannonballs.
- **Discovery**: the Discovery command on the high sea searches the waters around you. Finds pop up with their picture and go into the logbook window (natural wonders, animals, plants, relics, shells), and you get the "Discovery Logbook" record. The Adventurer Alliance search-and-authenticate quests (Termites, Violet, the Colosseum of Rome, Gorillas, Baboons...) now run: search at the place the quest names (or anywhere for the ones without a place) and bring the logbook back.
- **Pirates on the high sea come after you**: raiders and pirate fleets that spot you sail up and demand silver (the client's pay-or-fight box). Pay and they leave you alone for a while; refuse and the battle starts. You get a few seconds of peace when you reach the open sea and after a battle.
- **NPC ships show up again**: every NPC ship was sent the masts of the starter ship, and the client quietly drops a ship whose masts are missing from its hull, so only starter-hull pirates were drawn (merchants, frigates, the enemies of ocean battles and most fleets were invisible). Each hull now gets its own masts from the client models.
- **NPC ships stay on the water**: roaming traders and patrolling ships in coastal waters picked random spots around their home, often inside the island, where the terrain hid them. They now only sail across open water (worked out from the island models), and 31 ships that started on land were moved out to sea.

### Towns and NPCs
- **Refresh players button** (battle toolbar) now sends the players around you again instead of doing nothing.
- **The Veteran and the Skill Tutor (Battle and Trade)** stand at the foot of the stairs up to Athens City. The Veteran's storyline (Asking for enlightenment, Lesson One, Retraining...) and the life-skill training quests are back.
- **145 more quest NPCs** that the client's quest links point to (Coaching Quest Guide, Thomas, Kratos, Glaucus, Stan, Akbar, Atos.Willtay, the Atlantis and Lima NPCs...) now stand in their towns, and the quests that were missing because nobody could give or take them are back. Their exact spots were kept by the original server, so they stand near the other NPCs of their town. Event NPCs without a quest the emulator can run (Natasha and the other holiday envoys) are left out, so towns are no longer full of NPCs that only say "Good day", and the added NPCs wear a few different townsfolk outfits.
- **Taverns have their staff**: the Tavern Boss and the barmaids used player head models and so were invisible; they now wear NPC bodies. The Athens tavern staff no longer also stand outside the tavern door.
- **Athens tavern door** has its door marker, and leaving the tavern puts you on the street again (you landed just off the walkable ground, which showed an empty sky until you moved). Three other scene exits that dropped you off the walkable ground were moved too.
- **The Athens priest** stands behind the lectern on the altar platform, facing the pews.
- **Tower info window** (Golden Tower button) shows the server's best floor, your best floor, the current floor and its prizes.
- **GM `/ui <keyword>`** sends a raw interface keyword to your own client, for testing client windows. **GM `/sinkall`** sinks every enemy ship in your scene.

## 2026-10-18

### Character and equipment
- **VIP 1-7**: spending mall points earns VIP points (1 per point), and your VIP level follows the client's VIP table (VIP 1 at 10,000 up to VIP 7 at 1,050,000 points). The VIP window shows your level and points.
- **Levelling a skill** no longer pops open the Command Interface.
- **Damage numbers** float over whoever gets hit again, on land and at sea (the game's number font has no minus sign, so numbers with one were never drawn).
- **Equip sound** matches the item: clothes and hats use the clothing sound, weapons and other gear the equip sound.
- **Picking up loot** plays the pick-up sound and says what you obtained.
- **Items that need one of several skills** (e.g. Laborer Uniform) show the skill requirement in their tooltip and turn red when you don't meet it.
- **Level on the portrait** shows again (also for characters without a profession).
- **Exp bar** above the F1-F12 keys fills as your skills gain experience; hover it to see "Skill Exp: gained/needed"
  for the next level. The bar in Stats > Skill moves too.
- **Shortcut keys** (F1-F12) keep their skills and items when you change map.
- **Weapon skills** on the shortcut bar and in the Command Interface show red (unusable) unless you hold the weapon
  they need, and only on the kind of map they work on (land skills turn red at sea).
- **Equip sound** plays when you put gear on or take it off, by right-click or by dragging.
- **Casual dresses** show their own outfit when worn (European male: the long robe from its icon).
- **Swords and blades** wear a model that matches their icon, and the High-level/Light/Broad weapon versions have an
  icon and a model. **Guns** use the pistol stance again.
- **Nobility Equipment** window lists all 16 attributes (HP, SP, Attack, Defense, Accuracy, Dodging, Agility, Physique,
  Shocking Strike, Critical Resist, Damage Bonus/Reduction, Technique, Parry, Reaction, Willpower) with the totals of
  your worn nobility pieces. The rank set bonuses apply at 4 and 7 pieces (Baron .. Marquis) and count toward your
  stats.
- **General equipment rebuild** (FG000001-015): activate and lock lines, three free rebuilds a day, then Modification
  Stones or Special Offer Vouchers, Save/Cancel.
- **Star soul batch rebuild** rolls ten results at once and keeps the one you pick.
- **Skill Tutor**: Learn skills closes the talk window, so the tutor window (with your learned skills lit) is in front.
- **Skill names in messages** use the game's names ("You learned Rapid Fire.", "Shooting reached level 6.").
- **Command Interface**: learned stunts show under "Activate skill" (land stunts on On Land, sea stunts on At Sea) and
  gestures play when clicked.
- **Pet portrait** shows in the pet frame.
- **Buying and receiving items** (shops, stalls, the item mall, quest rewards, GM gifts) plays the item sound and says
  what you got.
- **Tools and other non-consumables** (pickaxe, hoe ...) are no longer used up when you right-click them.
- **Item Mall**: the tabs (Experience, Refine, Sea Battle, Land Battle, Items, Clothes, Pets) show only their goods,
  and pet whistles, Treasures from the Heaven, the Treasure Key, the Shopping Card and Voyager's Times have icons.
- **Character creation**: the second figure (the woman in the beige dress) no longer has a floating head; she now
  wears the white blouse and laced vest. Characters created with the old dress are fixed when they log in.
- **Character creation skill list** stays inside its box (four rows and a scroll bar) instead of spilling over the
  Previous button. Players need the updated client file `RESOURCE\INI\window\control.ini` (`[Other_00000532]`
  `RectWork=0, 0, 248, 192`; the stock file says 292).
- **Male and female clothes**: outfits and hats meant for one sex ("Male ...", "Female ...", "(M)", "(F)", "for Woman",
  Baron/Baroness Captain Dress and so on) can only be worn by that sex, so women no longer wear men's meshes with a
  floating head. Unisex items work for both. Anything of the other sex you already wear goes back to your bag when
  you log in.

### Ships and sea
- **Sails** no longer have holes on an undamaged ship; holes now follow sail damage.
- **Provisions** go into the ship's hold, are eaten at sea, and you can't sail off without them.
- **Sailor Recruitment window** (slider, Heal, Optimal/Max) opens from the Tavern Boss and the Shipyard. The Tavern Boss
  also recruits the necessary or all sailors, heals and dismisses them.
- **Ship rebuild VIEW** shows the new ship with its own hull, masts and name.
- **No more instant sinking**: a ship only sinks when her durability reaches 0 (losing all healthy sailors no longer
  sinks her), and one volley from an NPC ship takes at most a fifth of your durability.
- **Warning at sea** when you sail out with less than a quarter of your durability or no healthy sailors.
- **Friendly traffic** in every harbour sea and on the high seas: each city's own Trading Ship, Light Merchant Ship
  and Light Frigate (Athens, Venice, Genoa, Seville, Lisbon, Istanbul, Alexandria, Beirut, Tripoli, Barcelona ...),
  and a Merchant Ship and Patrol Ship off the other ports. They never attack and can't be shot or mined.
- **Engaging ships**: clicking an enemy ship keeps your ship firing whenever a battery is loaded and the target is in
  its arc, instead of a single broadside per click.
- **Sinking** no longer takes your mounted cannons; ships that lost them get their starting cannons back once.
- **Sailing out to the high seas** puts you right next to your port's harbour instead of far away from it.
- **Pirate ships are beatable**: hull and cannons now scale with the ship's level (a level 1 pirate has 150 hull and
  2 cannons, armed ships and flagships more), and pirate cannons hit less often (45% + 2% per level) and for 60% of
  the damage, so a starter ship can win against low-level pirates.
- **Sea instances** (Crete and North Sea pirate instances) put you on your ship instead of walking on the water.
- **NPC windows close** when you sail off or change map (the Docker no longer stays open at sea).
- **Steering**: a sharp turn at sea makes your ship sail a curve (its turning circle) instead of sliding sideways, and
  ships sail a bit slower (base speed 9 instead of 14; pirates chase at 7).

### Towns and NPCs
- **Hurricane Island**: sail to Hurricane Island on the high seas (1066, -483, the island's mark on the sea map) and
  click its dock to land on Hurricane Island Beach; the beach's Offshore exit sails you back out. On the beach the
  marked entrances take you into the Hurricane Island Mine Cave and Battle-Torn Forest instances (leaving the instance
  brings you back to the beach). The Hurricane Island Explorer in Jamestown and New Amsterdam harbours can also take
  you there once a day (a Hurricane Island Order allows one more trip), and Captain Migna sails you back.
- **Sitting down**: the sit button makes your character sit down; press it again or walk to stand up.
- **Stalls** show the stall table with goods in front of the seller, for the seller and everyone around. Type
  `/booth 1` to `/booth 19` to pick one of the 19 booth looks (weapons, clothes, ship parts, big market stands ...);
  your choice is saved and changes an open stall right away.
- **Killed animals and monsters** stop where they are and play their death animation straight away instead of walking on.
- **Talking to NPCs**: you walk up to the NPC first instead of talking from far away, and you both play a talk animation.
- **NPCs** no longer show an HP bar.
- **Athens Tavern** is open, with its Tavern Boss, Wendora, Pamafes and Helen. Players need the updated client file
  `RESOURCE\ini\scenes\1900026.ini` (a copy of `1018004.ini`) or the tavern shows grey shapes where Valentine's
  decorations with missing textures used to be.
- **Priests** heal you for a small fee and take donations (donations raise your reputation). Monks, healers and the
  battlefield Goddess of Healing / Quick Fix Tower heal for free; battlefield Emergency Repair Booths repair your ship.
- **Identifiers** verify Authenticating Articles from your bag for reputation.
- **Moneylenders** in the Asian ports keep your silver like a bank.
- **Material Exchange Staff** (Seville, Zhigu) also exchanges Blood Rose and Power Aquamarine fragments, diamonds
  (3 of a grade for 1 of the next) and socket stones (3 for a Superior one), next to the materials window.
- 93 duplicate NPCs made from map labels (Weapon Store, Items Store, Bank, Church ...) were removed; the real
  shopkeepers stand at the same spots.
- **NPC windows** close when you walk away, and "Goodbye" is always the last line of an NPC menu.

### Guilds, teams and activities
- **Game Masters and the Help window**: list GM account or character names under `"GameMasters"` in
  `config/emulator.json`. GMs get the client's GameMaster flag, and only they can use the slash commands (/item,
  /money, /go ...; while the list is empty everyone still can). Players open Help, pick what they need and press Next:
  online GMs are told and see the player's messages. GMs answer with `/gm r <player> <text>`, see open requests with
  `/gm list`, take one with `/gm take <player>` and end it with `/gm close <player>`, which opens the rating window
  for the player (ratings go to the GM and the log).
- **GM tools** (type them in chat, or as `/sc <command>` like the original GM client): `/kick <player> [reason]`,
  `/mute <player> <minutes>`, `/unmute`, `/ban <account or character> <hours|perm> [reason]` (the client shows "Your
  account has been banned!"), `/unban`, `/notice <text>` to everyone, `/goto <player>`, `/summon <player>` (across
  maps), `/hide` / `/unhide`, `/info <player>`, `/give <player> <item> [count]`, `/givemoney <player> <amount>`,
  `/healplayer <player>`.
  `/ships` lists the ships in your sea and `/engage <ship id>` attacks one. GMs skip the chat length and speed limits, can open the siege war settings, and get the
  client's own GM chat commands (/CF, /ShowPropVec, /SetCamSpeed ...) and Ctrl+E action editor.
- **Rankings** (Integrated Guild Strength, Land Battle Lord, Ocean Overlord, Honored Officer, Solo Arena, Tempering
  Master) list the top ten players (everyone counts, also with a score of 0), and the bottom line shows your own name
  and score.
- **Guild construction quests** at the guild hall: take a commercialization or industrialization quest, collect the
  goods and hand them in through the Submit Goods window. A finished quest raises the guild's Economic or Industrial
  degree, your contribution and the guild's vigor.
- **Daily battlefield quests** at the Practice Field Administrators (St. Deus and Palenque): pick a tier, pay it, and
  your next reward on that battlefield is raised. One a day.
- **Virtual teammates** from the team window join your team and show in the team frame.
- **Chat invites** and **cross-server teams** work (cross-server teams are normal teams on one server).
- **Switch Team** in the system settings toggles team invitations.
- The **Society Welfare** window no longer pops up at login or when guild vigor changes.

## 2026-10-17 (evening)

- **Clothes and hats show on the character.** Every wearable outfit now has a model: upgraded versions (High-Level, Refined,
  Superior) wear their base outfit, and items the client has no outfit for wear the tier outfit for their level (marked as a
  guess in the data). The European and Arabian casual dresses stay the default look on purpose.
- **Alt label without a profession** shows "Adventurer" and the character's level.
- **55 high-seas harbours** - San Francisco, San Diego, Seward, Cruz, Salinas, Magellan Supply Station, Mogadishu, Muscat,
  Basra and Tourane can now be entered.
- **Pets walk after you** instead of teleporting.
- **Ship rebuild** preview gets its ship model and no longer shows the NPC's text through the window.
- **Axe and gun stances** were swapped and are fixed.
- **Learning skills and stunts** shows an Upgrade message.
## 2026-10-17 (later)

### Combat and targeting
- **Target panel** - clicking a monster, NPC, ship or player opens the client's target frame with portrait, name and HP.
- **Portraits** - your own portrait shows next to the HP bar; monsters, ships and players have portraits.
- **TAB targeting and the AFK system** - TAB picks the nearest monster, and the AFK window's auto-attack fights on its
  own. Dead monsters are skipped.
- **Damage numbers** are sent; the client shows them when "show damage" (`TopBloody`) is on in the system settings.
- **Alt** shows profession and level over characters that have a profession.

### Movement
- Smoother movement for players, monsters and NPC ships: the server sends only destinations, so the client no longer
  snaps characters back to an estimated position.
- **Auto-path** works: the map's destination list fills in and the character walks the route.

### Gear
- Drag and drop between the equipment bag and worn slots equips and takes off gear; right-click takes gear off.
- 175 hats and helmets were marked as clothes; they now go into the hat slot.
- Portable battle bag, battle revive countdown, level-up notice.

### Item mall
- The mall (Ctrl+B, "Item Mall") opens with 136 goods: the 105 items the client's texts say come from the Item Mall,
  plus 31 premium pet whistles (Shadow Reaper and others), which go straight into the pet list.
- Paid with game points (`/points <n>` for GMs); remaining points are shown. Prices are guesses and live in
  `data\game\bbo\mall.json`.

### Instances
- The instance window lists all 27 instances with entrance, difficulty and chances left; clicking one shows its
  description, entrance and loot.

### High seas
- 35 harbours can be entered from the open sea (was 14), including London, Amsterdam, Hamburg, Bordeaux, Oslo,
  Stockholm, Reykjavik, Dakar, Cape Town, Bombay and Edo.
- Crossings between the Atlantic/Mediterranean map and the Indian Ocean/Asia/Pacific map at the Cape of Good Hope,
  the Magellan Strait and Iceland.
- 67 roaming pirate and merchant ships, two off each harbour, using that city's own ship types. They spawn and patrol
  only on spots checked in the client to be open water (`data\game\bbo\oceanwater.json`).
- NPC ships on the high seas are now visible in the client, and send hull, sail and crew values.

### NPCs
- NPC talk options come from the client's own menus; role NPCs use their city's menus.
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
