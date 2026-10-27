using System.Linq;
using UnityEngine;

namespace Octopusdream
{
    public sealed partial class PrototypeGame
    {
        void HostUpdate(float dt)
        {
            dt = Mathf.Min(dt, .05f);
            foreach (var p in players.Values)
            {
                var s = p.State;
                s.revealed = Mathf.Max(0f, s.revealed - dt);
                s.blinded = Mathf.Max(0f, s.blinded - dt);
                s.scanCooldown = Mathf.Max(0f, s.scanCooldown - dt);
                s.shotCooldown = Mathf.Max(0f, s.shotCooldown - dt);
                if (s.bot) UpdatePracticeBot(p);
                bool canMove = !s.caught && !s.editing && Phase != RoundPhase.Finished && !(Phase == RoundPhase.Hiding && s.diver);
                Vector3 move = canMove && Time.unscaledTime - p.InputAt < .5f ? p.InputMove : Vector3.zero;
                move.y *= .65f;
                p.Move(move * swimSpeed, dt);
                Vector3 clamped = p.transform.position;
                Vector3 min = arenaCenter - arenaSize * .5f + Vector3.one * .7f;
                Vector3 max = arenaCenter + arenaSize * .5f - Vector3.one * .7f;
                clamped.x = Mathf.Clamp(clamped.x, min.x, max.x);
                clamped.z = Mathf.Clamp(clamped.z, min.z, max.z);
                clamped.y = Mathf.Clamp(clamped.y, arenaCenter.y - arenaSize.y * .5f + .05f, max.y - 1.6f);
                if ((clamped - p.transform.position).sqrMagnitude > .001f) p.Teleport(clamped);
                s.position = p.transform.position;
                p.transform.rotation = Quaternion.Euler(0f, s.yaw, 0f);
            }

            if (Phase == RoundPhase.Hiding || Phase == RoundPhase.Hunting)
            {
                remaining = Mathf.Max(0f, remaining - dt);
                if (remaining <= 0f)
                {
                    if (Phase == RoundPhase.Hiding)
                    {
                        Phase = RoundPhase.Hunting;
                        foreach (var p in players.Values) p.State.editing = false;
                        editing = false;
                        cursorFree = false;
                        remaining = huntDuration;
                        notice = "잠수부가 수색을 시작했습니다.";
                        BroadcastWorld();
                    }
                    else Finish("산소가 소진됐습니다. 문어 팀 승리!");
                }
            }
            if (Phase == RoundPhase.Hunting)
            {
                foreach (var octopus in players.Values)
                {
                    if (octopus.State.diver || octopus.State.caught || octopus.State.inkUsed || octopus.State.hiddenScore >= 100f || octopus.State.revealed > 0f) continue;
                    bool evading = players.Values.Any(diver => diver.State.diver && !diver.State.caught && diver.State.blinded <= 0f &&
                        CanSee(diver, octopus, scanRange, 48f) && Vector3.Angle(diver.Forward, octopus.Eye - diver.Eye) > 4f);
                    if (evading) octopus.State.hiddenScore = Mathf.Min(100f, octopus.State.hiddenScore + dt * hiddenScorePerSecond);
                }
            }
            if (Time.unscaledTime >= snapshotAt)
            {
                snapshotAt = Time.unscaledTime + .1f;
                Broadcast(WorldMessage(), false);
            }
        }

        void StartRound()
        {
            if (practice && players.Count == 1)
            {
                var hunter = AddPlayer(2, "연습 잠수부", true);
                hunter.State.volunteer = !Local.State.volunteer;
                AddPlayer(3, "연습 문어 A", true);
                AddPlayer(4, "연습 문어 B", true);
            }
            if (players.Count < 2) { notice = "플레이어가 2명 이상 있어야 시작할 수 있습니다. 혼자서는 연습 모드를 이용하세요."; return; }
            hunterCount = Mathf.Clamp(hunterCount, 1, players.Count - 1);
            var candidates = players.Values.OrderByDescending(p => p.State.volunteer).ThenBy(p => Random.value).ToList();
            if (practice)
            {
                hunterCount = 1;
                ulong wanted = Local.State.volunteer ? transport.LocalId : 2;
                candidates = candidates.OrderByDescending(p => p.State.id == wanted).ToList();
            }
            var chosen = candidates.Take(hunterCount).Select(p => p.State.id).ToHashSet();
            int index = 0;
            foreach (var p in players.Values)
            {
                var s = p.State;
                s.diver = chosen.Contains(s.id);
                s.caught = false; s.editing = false; s.inkUsed = false;
                s.hiddenScore = s.revealed = s.blinded = s.scanCooldown = s.shotCooldown = 0f;
                s.pitch = 0f;
                p.InputMove = Vector3.zero;
                p.Controller.enabled = true;
                p.Teleport(Spawn(index++));
                s.position = p.transform.position;
                p.Appearance.SetRole(s.diver);
            }
            roundNumber++;
            Phase = RoundPhase.Hiding;
            remaining = hideDuration;
            result = "";
            editing = false;
            cursorFree = false;
            yaw = Local.State.yaw; pitch = 0f;
            transport.SetJoinable(false);
            BroadcastWorld();
        }

        void ReturnToLobby()
        {
            if (practice)
                foreach (var p in players.Values.Where(p => p.State.bot).ToArray()) RemovePlayer(p.State.id);
            int index = 0;
            foreach (var p in players.Values)
            {
                var s = p.State;
                s.diver = s.volunteer = s.caught = s.editing = s.inkUsed = false;
                s.hiddenScore = s.revealed = s.blinded = s.shotCooldown = s.scanCooldown = 0f;
                p.InputMove = Vector3.zero;
                p.Controller.enabled = true;
                p.Teleport(Spawn(index++));
                s.position = p.transform.position;
                p.Appearance.SetRole(false);
            }
            Phase = RoundPhase.Lobby;
            roundNumber++;
            remaining = 0f;
            result = "";
            editing = false;
            cursorFree = true;
            transport.SetJoinable(true);
            BroadcastWorld();
        }

        void Finish(string outcome)
        {
            if (Phase == RoundPhase.Finished) return;
            result = outcome;
            Phase = RoundPhase.Finished;
            editing = false;
            cursorFree = true;
            foreach (var p in players.Values) { p.State.editing = false; p.InputMove = Vector3.zero; }
            BroadcastWorld();
        }

        void CheckCaptureWin()
        {
            if (!IsRound) return;
            if (!players.Values.Any(p => !p.State.diver && !p.State.caught)) Finish("모든 문어를 포획했습니다. 잠수부 팀 승리!");
        }

        bool CanSee(PrototypePlayer observer, PrototypePlayer target, float range, float halfAngle)
        {
            if (target == observer || target.State.caught || observer.State.caught || observer.State.blinded > 0f) return false;
            Vector3 delta = target.Eye - observer.Eye;
            float distance = delta.magnitude;
            if (distance > range || distance < .01f || Vector3.Angle(observer.Forward, delta) > halfAngle) return false;
            return !Physics.Raycast(observer.Eye, delta / distance, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        void Shoot(PrototypePlayer shooter)
        {
            var s = shooter.State;
            if (!s.diver || s.caught || Phase != RoundPhase.Hunting || s.shotCooldown > 0f) return;
            s.shotCooldown = 1.1f;
            Ray ray = new Ray(shooter.Eye, shooter.Forward);
            float nearest = harpoonRange;
            if (Physics.Raycast(ray, out var wall, nearest, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) nearest = wall.distance;
            PrototypePlayer victim = null;
            foreach (var candidate in players.Values)
            {
                if (candidate == shooter || candidate.State.diver || candidate.State.caught) continue;
                if (candidate.Appearance.TryRaycastSurface(ray, out float distance, out _) && distance < nearest)
                {
                    nearest = distance;
                    victim = candidate;
                }
            }
            Vector3 endpoint = ray.GetPoint(nearest);
            if (victim != null)
            {
                victim.State.caught = true;
                victim.State.editing = false;
                victim.InputMove = Vector3.zero;
                victim.Controller.enabled = false;
                Effect("capture", victim.Eye, Vector3.zero);
                CheckCaptureWin();
            }
            Effect("shot", shooter.Eye + shooter.transform.right * .18f - Vector3.up * .12f, endpoint);
        }

        void Scan(PrototypePlayer caster)
        {
            var s = caster.State;
            if (Phase != RoundPhase.Hunting || !s.diver || s.caught || s.blinded > 0f || s.scanCooldown > 0f) return;
            s.scanCooldown = scanCooldown;
            foreach (var p in players.Values)
                if (!p.State.diver && CanSee(caster, p, scanRange, 48f)) p.State.revealed = scanDuration;
            Effect("scan", caster.Eye, Vector3.zero);
        }

        void Ink(PrototypePlayer caster)
        {
            var s = caster.State;
            if (Phase != RoundPhase.Hunting || s.diver || s.caught || s.inkUsed || s.hiddenScore < 100f) return;
            s.inkUsed = true;
            s.editing = false;
            foreach (var p in players.Values)
                if (p.State.diver && !p.State.caught && Vector3.Distance(p.Eye, caster.Eye) <= inkRadius)
                    p.State.blinded = inkDuration;
            Effect("ink", caster.Eye, Vector3.zero);
        }

        void SampleUnder(PrototypePlayer caster)
        {
            if (Physics.Raycast(caster.transform.position + Vector3.up * .15f, Vector3.down, out var hit, 12f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                SurfaceColorSampler.TrySample(hit, out var color))
            {
                color.a = 1f;
                caster.Appearance.SetBaseColor(color);
                Broadcast(new PrototypeMessage { kind = "fill", id = caster.State.id, color = color });
                if (caster == Local) notice = "발밑 표면의 색을 복사했습니다.";
            }
        }

        void UpdatePracticeBot(PrototypePlayer p)
        {
            p.InputAt = Time.unscaledTime;
            p.InputMove = Vector3.zero;
            if (p.State.diver)
            {
                p.State.yaw = Mathf.Repeat(Time.time * 25f, 360f);
                p.State.pitch = 0f;
                // A non-firing training diver lets a solo octopus practice vision avoidance and ink.
                if (Phase == RoundPhase.Hunting && p.State.scanCooldown <= 0f && Time.time % 25f < .05f) Scan(p);
            }
        }

        void Effect(string name, Vector3 from, Vector3 to)
        {
            var m = new PrototypeMessage { kind = "fx", text = name, vector = from, vector2 = to };
            ApplyEffect(m);
            Broadcast(m);
        }

        void ApplyEffect(PrototypeMessage m)
        {
            var go = new GameObject("Effect " + m.text);
            go.layer = 2;
            var effect = go.AddComponent<PrototypeTransient>();
            if (m.text == "shot")
            {
                var line = go.AddComponent<LineRenderer>();
                line.positionCount = 2;
                line.SetPositions(new[] { m.vector, m.vector2 });
                line.startWidth = .025f; line.endWidth = .01f;
                line.material = Lit(new Color(1f, .8f, .4f));
                effect.Lifetime = .2f;
            }
            else
            {
                go.transform.position = m.vector;
                if (m.text == "ink")
                {
                    for (int i = 0; i < 12; i++)
                    {
                        var blob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        blob.layer = 2;
                        Destroy(blob.GetComponent<Collider>());
                        blob.transform.SetParent(go.transform, false);
                        blob.transform.localPosition = Random.insideUnitSphere * 2.3f;
                        blob.transform.localScale = Vector3.one * Random.Range(1.2f, 2.7f);
                        blob.GetComponent<Renderer>().material = Lit(new Color(.025f, .035f, .075f));
                    }
                    effect.Lifetime = inkDuration;
                }
                else
                {
                    var line = go.AddComponent<LineRenderer>();
                    line.loop = true; line.positionCount = 48; line.startWidth = line.endWidth = .06f;
                    line.useWorldSpace = false;
                    line.material = Lit(m.text == "scan" ? new Color(1f, .25f, .15f) : new Color(.2f, 1f, 1f));
                    for (int i = 0; i < 48; i++)
                    {
                        float angle = i * Mathf.PI * 2f / 48;
                        line.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
                    }
                    effect.Lifetime = .65f;
                    effect.Expansion = m.text == "scan" ? 15f : 3f;
                }
            }
        }
    }

    public sealed class PrototypeTransient : MonoBehaviour
    {
        public float Lifetime = 1f, Expansion;
        float elapsed;
        void Update()
        {
            elapsed += Time.deltaTime;
            if (Expansion > 0f) transform.localScale = Vector3.one * (1f + Expansion * elapsed);
            if (elapsed >= Lifetime) Destroy(gameObject);
        }
        void OnDestroy()
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
                if (r.sharedMaterial != null) Destroy(r.sharedMaterial);
        }
    }
}
