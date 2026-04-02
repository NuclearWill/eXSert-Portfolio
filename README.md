# eXSert
**eXSert** is a third-person character-action prototype set aboard a derelict research airship. Master stance-based combat, aerial launchers, guard-based counters, and fast traversal while fighting through autonomous security drones as the ship gradually opens up around you.

---

# Portfolio Overview
Exset has a few programmers, myself included, who have made numerous scripts and have had to also had to edit each other's scripts from time to time. For the best look at what scripts best reflect my programming ability, look at the scripts within Assets/Scripts/Progression. Within the Progression folder, the Tutorial Handler, Progression Zone, BasicEncounter, and Wave scripts are written exclusively by me and the other scripts within that folder have been originally written by me with some minor edits added on by other people. Each script has a comment at the top which is meant to summarize the purpose of the script and within their script body is other comments to help explain the intended process. For scripts that contain foreign edits, I will functions and code blocks that are original

Additional scripts that are completely original to me include:
1. Assets/Scripts/Editor/CriticalreferenceDrawer which sets up a CriticalReference property. It's purpose is to designate serialized variables as "critical", requiring them to be assigned before allowing the level to be played. It includes additional functionality to help designers see what reference is missing.
2. Assets/Scripts/ScriptableObjects/SceneAsset which creates a user made Scene scriptable object which correlates with Unity's Scenes. Originally had more functionality however it eventually became too bloated and it was moved to the SceneLoader static class instead, leaving SceneAsset for purely data management and to help with scene management.
3. Assets/Scripts/Singeltons contains the Singleton abstract class that I wrote which many systems use to stay organized. The folder also includes SceneSingleton which is a special version made for ProgressionManager for its unique need that each level scene requires one instance of ProgressionManager.
4. Assets/Scripts/EnemyFactory contains the Enemy Object pooler singleton and Enemy Spawn Markers which I made. They help improve the performance of the game and reduce the memory cost. 
