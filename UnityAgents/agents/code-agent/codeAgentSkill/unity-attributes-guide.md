# Unity Attributes Checklist

All Unity C# scripts must obey the following rules:

## Serialization

- Public fields are serialized by default; use `[HideInInspector]` to hide or `[NonSerialized]` to prevent serialization.
- Private fields must use `[SerializeField]` to be visible in the Inspector.
- Use `[Serializable]` on custom classes/structs that need to appear in the Inspector.
- Use `[CreateAssetMenu]` on ScriptableObject subclasses for asset creation menu.

## Component Requirements

- `[RequireComponent(typeof(XXX))]` when the script depends on another component.
- `[DisallowMultipleComponent]` to prevent multiple instances on the same GameObject.
- `[ExecuteAlways]` or `[ExecuteInEditMode]` only when truly needed.

## Editor & Context

- `[MenuItem("GameObject/...")]` for Editor menu items (must be static).
- `[ContextMenu("Action Name")]` for right-click context actions in the Inspector.
- `[ContextMenuItem("Label", "MethodName")]` for per-field context actions.

## Lifecycle

- Awake() — called once when script instance is loaded, for initialization.
- Start() — called before first Update, after all Awake().
- Update() — called every frame (avoid heavy logic here).
- FixedUpdate() — called at fixed timestep, use for physics.
- LateUpdate() — called after all Update() calls.
- OnEnable() / OnDisable() — for activation/deactivation cleanup.
- OnDestroy() — for cleanup when destroyed.
- OnValidate() — called when values change in Inspector (Editor only).

## Coroutines

- Use `StartCoroutine(IEnumerator)` to start, `StopCoroutine()` to stop.
- Always check `gameObject != null` or use `while (enabled)` patterns to avoid coroutine leaks.
- Use `yield return new WaitForSeconds()` for delays; `yield return null` for next frame.

## Performance

- Avoid `GetComponent<T>()` / `GameObject.Find()` in Update() — cache references.
- Use `CompareTag()` instead of `.tag == "string"` comparison.
- Prefer `StringBuilder` for runtime string concatenation.
- Avoid allocating garbage in Update() (no `new` allocations).

## Null Safety

- Use null-conditional (`?.`) and null-coalescing (`??`) operators.
- Always null-check GetComponent<T>() results.
- Check if `gameObject != null` in coroutines after yield return.
