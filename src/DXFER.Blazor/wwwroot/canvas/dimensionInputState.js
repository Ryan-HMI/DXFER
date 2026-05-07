export function getPendingPersistentDimensionEditId(dimensions, previousIds) {
  if (!previousIds || typeof previousIds.has !== "function" || !Array.isArray(dimensions)) {
    return null;
  }

  for (const dimension of dimensions) {
    const id = String(dimension && dimension.id || "");
    if (id && !previousIds.has(id)) {
      return id;
    }
  }

  return null;
}

export function getDefaultActiveDimensionKey(dimensions, requestedKey) {
  const keys = getDimensionKeys(dimensions);
  if (keys.length === 0) {
    return null;
  }

  return keys.includes(requestedKey)
    ? requestedKey
    : keys[0];
}

export function resolveActiveDimensionKey(dimensions, requestedKey, pendingKey = null, focusedKey = null) {
  const keys = getDimensionKeys(dimensions);
  const requested = requestedKey || null;
  const pending = pendingKey || null;
  const focused = focusedKey || null;

  if (keys.length === 0) {
    return {
      activeKey: null,
      pendingKey: pending
    };
  }

  if (pending && keys.includes(pending)) {
    return {
      activeKey: pending,
      pendingKey: null
    };
  }

  return {
    activeKey: keys.includes(focused) ? focused : keys.includes(requested) ? requested : keys[0],
    pendingKey: null
  };
}

export function getNextDimensionKey(dimensions, currentKey, reverse = false) {
  const keys = getDimensionKeys(dimensions);
  if (keys.length === 0) {
    return null;
  }

  const currentIndex = Math.max(0, keys.indexOf(currentKey));
  const offset = reverse ? -1 : 1;
  const nextIndex = (currentIndex + offset + keys.length) % keys.length;
  return keys[nextIndex];
}

export function getVisibleDimensionDescriptors(state) {
  const keys = Array.isArray(state.visibleDimensionKeys) && state.visibleDimensionKeys.length > 0
    ? state.visibleDimensionKeys
    : Array.from(state.dimensionInputs.keys());

  return keys
    .filter(key => state.dimensionInputs.has(key))
    .map(key => ({ key }));
}

export function getDimensionKeys(dimensions) {
  if (!Array.isArray(dimensions) || dimensions.length === 0) {
    return [];
  }

  return dimensions
    .map(dimension => dimension && dimension.key)
    .filter(key => Boolean(key));
}
