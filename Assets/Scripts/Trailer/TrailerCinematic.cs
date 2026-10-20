using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Airplane.FlightSimulation;
using Airplane.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace Airplane.Trailer
{
    [DefaultExecutionOrder(-500)]
    public sealed class TrailerCinematic : MonoBehaviour
    {
        [Serializable]
        public struct Shot
        {
            public Camera camera;
            public float start;
            public float duration;
        }

        private struct Fighter
        {
            public Transform Root;
            public AircraftGun[] Guns;
            public Collider[] Colliders;
            public Renderer[] Renderers;
            public MaterialPropertyBlock[] OriginalBlocks;
            public bool Destroyed;
            public float KillTime;
        }

        [SerializeField] private Shot[] shots;
        [SerializeField] private Transform[] aircraftRoots;
        [SerializeField] private Animation[] tracks;
        [SerializeField] private Graphic title;
        [SerializeField] private Graphic subtitle;
        [SerializeField] private float length = 52f;
        [SerializeField] private float propellerDegreesPerSecond = 2400f;
        [SerializeField] private Transform[] banditRoots;
        [SerializeField] private float[] banditKillTimes = { 29.5f, 33.2f };
        [SerializeField] private float fightStart = 23f;
        [SerializeField] private float fightEnd = 36f;
        [SerializeField] private float boreSightDegrees = 10.5f;

        [Header("MP4 Recording")]
        [SerializeField] private bool recordToMp4;
        [SerializeField] private string outputMp4Path = "Recordings/Trailer.mp4";
        [SerializeField] private int recordFps = 60;
        [SerializeField] private int recordSuperSize = 1;

        public static bool StartRecordingNextPlay;

        private readonly List<Transform> _propellers = new List<Transform>();
        private Fighter[] _fighters = System.Array.Empty<Fighter>();
        private Fighter[] _bandits = System.Array.Empty<Fighter>();
        private GameObject _hitSparks;
        private float _clock;
        private float _prevClock;
        private float _shotWait;
        private bool _isRecording;
        private Process _ffmpegProcess;
        private BinaryWriter _ffmpegStdin;
        private Texture2D _captureTex;
        private int _recordWidth;
        private int _recordHeight;
        private int _recordedFrames;
        private int _totalRecordFrames;
        private int _savedCaptureFramerate;
        private int _savedVSyncCount;

        public bool IsRecording => _isRecording;
        public string OutputMp4Path => outputMp4Path;

        [ContextMenu("Start MP4 Recording")]
        public void BeginMp4Recording()
        {
            if (_isRecording)
                return;

            _clock = 0f;
            _prevClock = 0f;
            _shotWait = 0f;
            UpdateBanditDestruction(0f, true);
            Apply(0f);

            int fps = Mathf.Clamp(recordFps, 15, 120);
            _totalRecordFrames = Mathf.Max(1, Mathf.RoundToInt(length * fps));
            _recordedFrames = 0;

            int scale = Mathf.Clamp(recordSuperSize, 1, 4);
            _recordWidth = Mathf.Max(2, (Screen.width * scale) & ~1);
            _recordHeight = Mathf.Max(2, (Screen.height * scale) & ~1);

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string fullOut = Path.IsPathRooted(outputMp4Path)
                ? outputMp4Path
                : Path.GetFullPath(Path.Combine(projectRoot, outputMp4Path));
            string dir = Path.GetDirectoryName(fullOut);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string args =
                $"-y -f image2pipe -vcodec png -r {fps} -i - " +
                $"-vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" -c:v libx264 -pix_fmt yuv420p -preset medium -crf 18 -movflags +faststart \"{fullOut}\"";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                _ffmpegProcess = Process.Start(psi);
                if (_ffmpegProcess == null)
                {
                    UnityEngine.Debug.LogError("Failed to launch ffmpeg process.");
                    return;
                }
                _ffmpegProcess.BeginErrorReadLine();
                _ffmpegStdin = new BinaryWriter(_ffmpegProcess.StandardInput.BaseStream);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("Could not start ffmpeg for MP4 recording: " + ex.Message);
                return;
            }

            _savedCaptureFramerate = Time.captureFramerate;
            _savedVSyncCount = QualitySettings.vSyncCount;
            QualitySettings.vSyncCount = 0;
            Time.captureFramerate = fps;

            _isRecording = true;
            UnityEngine.Debug.Log($"Recording trailer MP4 ({_recordWidth}x{_recordHeight} @ {fps}fps) to: {fullOut}");
            StartCoroutine(CaptureFramesLoop(fullOut));
        }

        [ContextMenu("Stop MP4 Recording")]
        public void StopMp4Recording()
        {
            if (!_isRecording)
                return;
            FinishRecording(null);
        }

        private IEnumerator CaptureFramesLoop(string fullOut)
        {
            var waitEnd = new WaitForEndOfFrame();
            while (_isRecording)
            {
                yield return waitEnd;
                if (!_isRecording)
                    break;

                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture(Mathf.Clamp(recordSuperSize, 1, 4));
                if (shot)
                {
                    byte[] png = ImageConversion.EncodeToPNG(shot);
                    if (png != null && png.Length > 0)
                        _ffmpegStdin.Write(png);
                    Destroy(shot);
                }

                _recordedFrames++;
                if (_recordedFrames >= _totalRecordFrames)
                {
                    FinishRecording(fullOut);
                    yield break;
                }
            }
        }

        private void FinishRecording(string fullOut)
        {
            if (!_isRecording)
                return;

            _isRecording = false;
            Time.captureFramerate = _savedCaptureFramerate;
            QualitySettings.vSyncCount = _savedVSyncCount;

            try
            {
                if (_ffmpegStdin != null)
                {
                    _ffmpegStdin.Flush();
                    _ffmpegStdin.Close();
                    _ffmpegStdin = null;
                }
                if (_ffmpegProcess != null)
                {
                    _ffmpegProcess.WaitForExit(10000);
                    _ffmpegProcess.Dispose();
                    _ffmpegProcess = null;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("Error closing ffmpeg stream: " + ex.Message);
            }

            if (_captureTex)
            {
                Destroy(_captureTex);
                _captureTex = null;
            }

            if (!string.IsNullOrEmpty(fullOut))
                UnityEngine.Debug.Log("Finished rendering trailer MP4: " + fullOut);

#if UNITY_EDITOR
            if (StartRecordingNextPlay)
            {
                StartRecordingNextPlay = false;
                UnityEditor.EditorApplication.isPlaying = false;
            }
#endif
        }

        private void OnDestroy()
        {
            if (_isRecording)
                FinishRecording(null);
        }

        private void Awake()
        {
            if (aircraftRoots != null)
            {
                var fighters = new List<Fighter>(aircraftRoots.Length);
                foreach (Transform root in aircraftRoots)
                {
                    if (!root)
                        continue;

                    root.localPosition = Vector3.zero;
                    fighters.Add(new Fighter
                    {
                        Root = root,
                        Guns = root.GetComponentsInChildren<AircraftGun>(true),
                        Colliders = EnsureColliders(root)
                    });

                    foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                        behaviour.enabled = false;

                    foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (child.name.IndexOf("Propeller", StringComparison.OrdinalIgnoreCase) >= 0)
                            _propellers.Add(child);
                    }
                }

                _fighters = fighters.ToArray();
            }

            _bandits = Collect(banditRoots);
            for (int i = 0; i < _bandits.Length; i++)
            {
                if (banditKillTimes != null && i < banditKillTimes.Length)
                    _bandits[i].KillTime = banditKillTimes[i];
                else
                    _bandits[i].KillTime = 29.5f + i * 3.7f;

                if (!_bandits[i].Root)
                    continue;
                foreach (Transform child in _bandits[i].Root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name.IndexOf("Propeller", StringComparison.OrdinalIgnoreCase) >= 0)
                        _propellers.Add(child);
                }
            }

            _hitSparks = Resources.Load<GameObject>("Particles/AircraftHitSparks");

            if (tracks != null)
            {
                foreach (Animation track in tracks)
                {
                    if (!track || !track.clip)
                        continue;

                    track.Play(track.clip.name);
                    foreach (AnimationState state in track)
                        state.speed = 0f;
                }
            }

            Apply(0f);

            if (recordToMp4 || StartRecordingNextPlay)
                BeginMp4Recording();
        }

        private void Update()
        {
            if (_propellers.Count > 0)
            {
                float step = propellerDegreesPerSecond * Time.deltaTime;
                foreach (Transform propeller in _propellers)
                    propeller.Rotate(Vector3.forward, step, Space.Self);
            }
        }

        private static Fighter[] Collect(Transform[] roots)
        {
            if (roots == null)
                return System.Array.Empty<Fighter>();

            var fighters = new List<Fighter>(roots.Length);
            foreach (Transform root in roots)
            {
                if (!root)
                    continue;

                root.localPosition = Vector3.zero;
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                var blocks = new MaterialPropertyBlock[renderers.Length];
                for (int r = 0; r < renderers.Length; r++)
                {
                    blocks[r] = new MaterialPropertyBlock();
                    if (renderers[r])
                        renderers[r].GetPropertyBlock(blocks[r]);
                }

                fighters.Add(new Fighter
                {
                    Root = root,
                    Guns = root.GetComponentsInChildren<AircraftGun>(true),
                    Colliders = EnsureColliders(root),
                    Renderers = renderers,
                    OriginalBlocks = blocks
                });

                foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                    behaviour.enabled = false;
            }

            return fighters.ToArray();
        }

        private static Collider[] EnsureColliders(Transform root)
        {
            Collider[] existing = root.GetComponentsInChildren<Collider>(true);
            if (existing != null && existing.Length > 0)
                return existing;

            MeshFilter[] meshes = root.GetComponentsInChildren<MeshFilter>(true);
            var added = new List<Collider>(meshes.Length);
            foreach (MeshFilter mf in meshes)
            {
                if (!mf || !mf.sharedMesh)
                    continue;
                BoxCollider box = mf.gameObject.AddComponent<BoxCollider>();
                added.Add(box);
            }

            return added.ToArray();
        }

        private void FireGuns()
        {
            if (_clock < fightStart || _clock >= fightEnd)
                return;

            _shotWait -= Time.deltaTime;
            if (_shotWait > 0f)
                return;

            _shotWait = 0.08f;
            Physics.SyncTransforms();
            var tracerColor = new Color(1.1f, 0.78f, 0.25f, 1f);
            for (int i = 0; i < _fighters.Length; i++)
                FireBore(_fighters[i], _bandits, tracerColor);
        }

        private void FireBore(Fighter shooter, Fighter[] targets, Color tracerColor)
        {
            if (!shooter.Root || shooter.Guns == null || targets == null)
                return;

            if (HasFriendlyInLineOfFire(shooter.Root))
                return;

            for (int g = 0; g < shooter.Guns.Length; g++)
            {
                AircraftGun gun = shooter.Guns[g];
                if (!gun)
                    continue;

                Vector3 origin = gun.Muzzle.position;
                Vector3 axis = gun.ShotAxisWorld;
                if (axis.sqrMagnitude < 1e-6f)
                    continue;

                axis.Normalize();
                Transform target = BestBoreTarget(origin, axis, targets);
                if (!target)
                    continue;

                origin += axis * 2.4f;
                Vector3 toTarget = (target.position - origin).normalized;
                Vector3 aimAxis = Vector3.RotateTowards(axis, toTarget, 5.5f * Mathf.Deg2Rad, 0f);
                Vector3 dir = Quaternion.AngleAxis(UnityEngine.Random.Range(-0.35f, 0.35f), Vector3.up) * aimAxis;
                float speed = 640f;
                float maxDist = 700f;
                float life = 0.48f;

                if (TryRaycastAircraft(origin, dir, maxDist, shooter.Root, out RaycastHit hit, out bool hitFriendly))
                {
                    if (hitFriendly)
                        continue;

                    life = Mathf.Clamp(hit.distance / speed, 0.04f, life);
                    if (_hitSparks)
                        Instantiate(_hitSparks, hit.point, Quaternion.LookRotation(hit.normal));
                }

                AircraftProjectile tracer = AircraftProjectile.RentDefault(tracerColor, 0.1f);
                if (tracer)
                    tracer.LaunchKinematic(origin, dir * speed, life);
            }
        }

        private bool HasFriendlyInLineOfFire(Transform shooter)
        {
            Vector3 from = shooter.position;
            Vector3 forward = shooter.forward;
            for (int i = 0; i < _fighters.Length; i++)
            {
                Transform other = _fighters[i].Root;
                if (!other || other == shooter)
                    continue;

                Vector3 to = other.position - from;
                float along = Vector3.Dot(to, forward);
                if (along <= 0f || along > 260f)
                    continue;

                float perpSqr = to.sqrMagnitude - along * along;
                if (perpSqr < 49f)
                    return true;
            }

            return false;
        }

        private bool TryRaycastAircraft(
            Vector3 origin,
            Vector3 dir,
            float maxDist,
            Transform shooter,
            out RaycastHit closestHit,
            out bool hitFriendly)
        {
            closestHit = default;
            hitFriendly = false;
            RaycastHit[] hits = Physics.RaycastAll(origin, dir, maxDist);
            float bestDist = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider col = hits[i].collider;
                if (!col || IsUnder(col, shooter))
                    continue;

                bool friendly = false;
                for (int f = 0; f < _fighters.Length; f++)
                {
                    if (IsUnder(col, _fighters[f].Root))
                    {
                        friendly = true;
                        break;
                    }
                }

                bool enemy = false;
                if (!friendly)
                {
                    for (int b = 0; b < _bandits.Length; b++)
                    {
                        if (IsUnder(col, _bandits[b].Root))
                        {
                            enemy = true;
                            break;
                        }
                    }
                }

                if (!friendly && !enemy)
                    continue;

                if (hits[i].distance < bestDist)
                {
                    bestDist = hits[i].distance;
                    closestHit = hits[i];
                    hitFriendly = friendly;
                }
            }

            return bestDist < float.MaxValue;
        }

        private Transform BestBoreTarget(Vector3 origin, Vector3 axis, Fighter[] targets)
        {
            Transform best = null;
            float bestAngle = boreSightDegrees;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i].Destroyed)
                    continue;

                Transform root = targets[i].Root;
                if (!root)
                    continue;

                Vector3 to = root.position - origin;
                if (to.sqrMagnitude < 4f)
                    continue;

                float angle = Vector3.Angle(axis, to);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = root;
                }
            }

            return best;
        }

        private void UpdateBanditDestruction(float time, bool looped)
        {
            if (looped)
            {
                for (int i = 0; i < _bandits.Length; i++)
                {
                    if (!_bandits[i].Root)
                        continue;

                    _bandits[i].Destroyed = false;
                    Transform fire = _bandits[i].Root.Find("WreckFire");
                    if (fire)
                        Destroy(fire.gameObject);
                    AircraftWreckVisual wreck = _bandits[i].Root.GetComponent<AircraftWreckVisual>();
                    if (wreck)
                        Destroy(wreck);

                    if (_bandits[i].Renderers != null && _bandits[i].OriginalBlocks != null)
                    {
                        for (int r = 0; r < _bandits[i].Renderers.Length; r++)
                        {
                            Renderer ren = _bandits[i].Renderers[r];
                            if (ren)
                                ren.SetPropertyBlock(_bandits[i].OriginalBlocks[r]);
                        }
                    }
                }
            }

            for (int i = 0; i < _bandits.Length; i++)
            {
                if (_bandits[i].Destroyed || !_bandits[i].Root)
                    continue;

                if (time >= _bandits[i].KillTime && _bandits[i].KillTime > 0.01f)
                {
                    _bandits[i].Destroyed = true;
                    AircraftExplosion.Play(_bandits[i].Root.position);
                    AircraftWreckVisual.Apply(_bandits[i].Root.gameObject);
                }
            }
        }

        private static bool IsUnder(Collider collider, Transform root)
        {
            if (!collider || !root)
                return false;

            Transform hit = collider.transform;
            return hit == root || hit.IsChildOf(root);
        }

        private void LateUpdate()
        {
            _clock += Time.deltaTime;
            bool looped = false;
            if (length > 0.01f)
            {
                while (_clock >= length)
                {
                    _clock -= length;
                    looped = true;
                }
            }
            if (_clock < _prevClock)
                looped = true;
            _prevClock = _clock;

            Apply(_clock);
            UpdateBanditDestruction(_clock, looped);
            FireGuns();
        }


        private void Apply(float time)
        {
            if (tracks != null)
            {
                foreach (Animation track in tracks)
                {
                    if (!track)
                        continue;

                    foreach (AnimationState state in track)
                    {
                        state.enabled = true;
                        state.weight = 1f;
                        state.speed = 0f;
                        state.time = time;
                    }

                    track.Sample();
                }
            }

            Camera active = null;
            if (shots != null)
            {
                for (int i = 0; i < shots.Length; i++)
                {
                    Shot shot = shots[i];
                    if (time >= shot.start && time < shot.start + shot.duration)
                        active = shot.camera;
                }

                if (!active && shots.Length > 0)
                    active = shots[shots.Length - 1].camera;

                for (int i = 0; i < shots.Length; i++)
                {
                    Camera camera = shots[i].camera;
                    if (!camera)
                        continue;

                    bool on = camera == active;
                    camera.enabled = on;
                    AudioListener listener = camera.GetComponent<AudioListener>();
                    if (listener)
                        listener.enabled = on;
                }
            }

            SetAlpha(title, TitleAlpha(time));
            SetAlpha(subtitle, SubtitleAlpha(time));
        }

        private float TitleAlpha(float time)
        {
            float open = Fade(time, 1.15f, 2.7f, 6.3f, 7.7f);
            float close = Fade(time, 46.4f, 48.4f, length + 1f, length + 2f);
            return Mathf.Max(open, close);
        }

        private float SubtitleAlpha(float time)
        {
            return Fade(time, 47.6f, 49.4f, length + 1f, length + 2f);
        }

        private static float Fade(float time, float inStart, float inEnd, float outStart, float outEnd)
        {
            if (time < inStart || time >= outEnd)
                return 0f;
            if (time < inEnd)
                return Mathf.SmoothStep(0f, 1f, (time - inStart) / Mathf.Max(0.0001f, inEnd - inStart));
            if (time > outStart)
                return Mathf.SmoothStep(1f, 0f, (time - outStart) / Mathf.Max(0.0001f, outEnd - outStart));
            return 1f;
        }

        private static void SetAlpha(Graphic graphic, float alpha)
        {
            if (!graphic)
                return;

            Color color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }
    }
}
