export function RegisterEvent(ref, dotnetRef, eventName, methodName) {
    // call .net method
    ref.addEventListener(eventName, (e) => { dotnetRef.invokeMethodAsync(methodName, e) });
}
