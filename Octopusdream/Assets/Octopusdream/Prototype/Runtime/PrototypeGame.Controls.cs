using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Octopusdream
{
    public sealed partial class PrototypeGame
    {
        float editSoftness = 5f, lastSoftSent = -1f, softSendAt;
        float editBodyYaw, editBodyPitch;
        Transform viewHarpoon;

        void ReadControls()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            var local = Local;
            if (keyboard == null || mouse == null || local == null) return;
            if (local.State.diver || local.State.caught || Phase == RoundPhase.Finished)
            {
                if (editing) ReleasePainterTextFocus();
                editing = false;
            }

            if (editing && PainterHasTextFocus)
            {
                // RGB typing must not also trigger F/V/E or movement in the scene.
                if (keyboard.escapeKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                    ReleasePainterTextFocus();
                if (Time.unscaledTime >= inputAt)
                {
                    inputAt = Time.unscaledTime + .05f;
                    SendToHost(new PrototypeMessage { kind = "input", vector = Vector3.zero, yaw = editBodyYaw, pitch = editBodyPitch, sequence = ++inputSequence });
                }
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (editing) ToggleEdit();
                else cursorFree = !cursorFree;
            }
            if (keyboard.f1Key.wasPressedThisFrame) help = !help;
            if (keyboard.qKey.wasPressedThisFrame && local.State.caught) spectatorIndex++;
            if (keyboard.fKey.wasPressedThisFrame && CanEdit(local)) ToggleEdit();

            bool rotate = Cursor.lockState == CursorLockMode.Locked || mouse.rightButton.isPressed;
            if (rotate)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yaw = Mathf.Repeat(yaw + delta.x * .12f, 360f);
                pitch = Mathf.Clamp(pitch - delta.y * .12f, local.State.diver ? -80f : -35f, local.State.diver ? 80f : 70f);
            }
            if (keyboard.vKey.wasPressedThisFrame && CanEdit(local)) SendToHost(new PrototypeMessage { kind = "sample" });
            if (keyboard.eKey.wasPressedThisFrame)
            {
                if (Phase == RoundPhase.Lobby)
                {
                    if (Vector3.Distance(local.transform.position, lobbyPosition) < 3.5f)
                    {
                        if (editing) ToggleEdit();
                        SendToHost(new PrototypeMessage { kind = "volunteer" });
                    }
                    else notice = "중앙 작살 가까이에서 E를 눌러 술래에 지원하세요.";
                }
                else if (Phase == RoundPhase.Hunting)
                {
                    if (editing && !local.State.diver && !local.State.inkUsed && local.State.hiddenScore >= 100f) ToggleEdit();
                    SendToHost(new PrototypeMessage { kind = "skill", yaw = yaw, pitch = pitch, sequence = ++inputSequence });
                }
            }
            if (local.State.diver && !cursorFree && mouse.leftButton.wasPressedThisFrame && Phase == RoundPhase.Hunting)
                SendToHost(new PrototypeMessage { kind = "shot", yaw = yaw, pitch = pitch, sequence = ++inputSequence });

            if (editing)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > .01f) brushRadius = Mathf.Clamp(brushRadius + Mathf.Sign(wheel) * .015f, .04f, .45f);
                if (mouse.leftButton.isPressed && !OverInterface(mouse.position.ReadValue()) && Time.unscaledTime - lastPaintAt > .06f)
                {
                    if (local.Appearance.TryGetPaintDirection(viewCamera.ScreenPointToRay(mouse.position.ReadValue()), out Vector3 direction))
                    {
                        lastPaintAt = Time.unscaledTime;
                        SendToHost(new PrototypeMessage { kind = "paint", vector = direction, color = paintColor, value = brushRadius });
                    }
                }
                if (Mathf.Abs(editSoftness - lastSoftSent) > .01f && Time.unscaledTime > softSendAt)
                {
                    softSendAt = Time.unscaledTime + .12f;
                    lastSoftSent = editSoftness;
                    local.Appearance.SetSoftness(editSoftness);
                    SendToHost(new PrototypeMessage { kind = "soft", value = editSoftness });
                }
            }

            bool moveAllowed = !editing && !local.State.caught && Phase != RoundPhase.Finished &&
                !(Phase == RoundPhase.Hiding && local.State.diver) && (!cursorFree || Phase == RoundPhase.Lobby);
            Vector3 move = Vector3.zero;
            if (moveAllowed)
            {
                move.x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                move.z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                move.y = (keyboard.spaceKey.isPressed ? 1f : 0f) - (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed ? 1f : 0f);
                move = Quaternion.Euler(0f, yaw, 0f) * Vector3.ClampMagnitude(move, 1f);
            }
            float playerYaw = editing ? editBodyYaw : yaw;
            float playerPitch = editing ? editBodyPitch : pitch;
            local.State.yaw = playerYaw;
            local.State.pitch = playerPitch;
            if (!transport.IsHost && moveAllowed)
            {
                Vector3 prediction = move; prediction.y *= .65f;
                local.Move(prediction * swimSpeed, Time.deltaTime);
            }
            if (Time.unscaledTime >= inputAt)
            {
                inputAt = Time.unscaledTime + .05f;
                SendToHost(new PrototypeMessage { kind = "input", vector = move, yaw = playerYaw, pitch = playerPitch, sequence = ++inputSequence });
            }
        }

        void ToggleEdit()
        {
            if (Local == null || !CanEdit(Local)) return;
            editing = !editing;
            if (editing)
            {
                editBodyYaw = Local.State.yaw;
                editBodyPitch = Local.State.pitch;
                editSoftness = Local.State.softness;
                lastSoftSent = editSoftness;
                cursorFree = true;
            }
            else { cursorFree = Phase == RoundPhase.Lobby; ReleasePainterTextFocus(); }
            SendToHost(new PrototypeMessage { kind = "edit", flag = editing });
        }

        bool OverInterface(Vector2 screenPosition)
        {
            Vector2 point = new Vector2(screenPosition.x, Screen.height - screenPosition.y) / guiScale;
            float width = Screen.width / guiScale;
            return (point.x < 350f && point.y < (Phase == RoundPhase.Lobby ? 635f : 180f)) ||
                (editing && point.x > width - 330f) || point.y > Screen.height / guiScale - 85f;
        }

        void UpdateView()
        {
            if (viewCamera == null) return;
            if (!CanPlay) return;
            var local = Local;
            PrototypePlayer targetPlayer = local;
            if (local.State.caught)
            {
                var alive = players.Values.Where(p => !p.State.caught && !p.State.diver).ToArray();
                if (alive.Length == 0) alive = players.Values.Where(p => !p.State.caught).ToArray();
                if (alive.Length > 0) targetPlayer = alive[spectatorIndex % alive.Length];
            }
            bool firstPerson = local.State.diver && !local.State.caught;
            viewCamera.cullingMask = Phase == RoundPhase.Hiding && firstPerson ? 0 : ~0;
            if (firstPerson)
            {
                viewCamera.transform.SetPositionAndRotation(local.Eye, Quaternion.Euler(pitch, yaw, 0f));
            }
            else
            {
                Vector3 target = targetPlayer.Appearance.FocusPoint;
                Vector3 orbitOrigin = target + Vector3.up * (targetPlayer.Appearance.SurfaceContact > .5f ? .18f : 0f);
                Vector3 direction = Quaternion.Euler(pitch, yaw, 0f) * Vector3.back;
                float bodyWidth = targetPlayer.Appearance.WorldBounds.size.magnitude;
                float distance = editing ? Mathf.Max(2.65f, bodyWidth * 1.1f) : 4.2f;
                if (Physics.SphereCast(orbitOrigin, .15f, direction, out var hit, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    distance = Mathf.Max(.05f, hit.distance - .03f);
                Vector3 cameraPosition = orbitOrigin + direction * distance;
                viewCamera.transform.position = Vector3.Lerp(viewCamera.transform.position, cameraPosition, 1f - Mathf.Exp(-18f * Time.deltaTime));
                viewCamera.transform.LookAt(target);
            }
            if (viewHarpoon == null)
            {
                var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                shaft.name = "First person harpoon";
                Destroy(shaft.GetComponent<Collider>());
                shaft.transform.SetParent(viewCamera.transform, false);
                shaft.transform.localPosition = new Vector3(.3f, -.27f, .7f);
                shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                shaft.transform.localScale = new Vector3(.045f, .45f, .045f);
                shaft.GetComponent<Renderer>().material = Lit(new Color(.7f, .8f, .85f));
                viewHarpoon = shaft.transform;
            }
            viewHarpoon.gameObject.SetActive(firstPerson && Phase != RoundPhase.Hiding);
            harpoonStand.gameObject.SetActive(Phase == RoundPhase.Lobby);
        }
    }
}
