import { Algorithm, SolveStatus } from "./vendor/planegcs/dist/index.js";

const converged = status => status === SolveStatus.Success || status === SolveStatus.Converged;
const accepted = (wrapper, status) => converged(status) && !wrapper.get_gcs_conflicting_constraints().length;

function restart(wrapper) {
    wrapper.destroy_gcs_module();
    wrapper.gcs = new wrapper.module.GcsSystem();
}

function attempt(wrapper, primitives, algorithm) {
    const preferences = primitives.filter(primitive => primitive.temporary === true);
    wrapper.push_primitives_and_params(primitives.filter(primitive => primitive.temporary !== true));
    wrapper.set_max_iterations(200);
    if (preferences.length) {
        const hardStatus = wrapper.solve(algorithm);
        if (wrapper.get_gcs_conflicting_constraints().length) return hardStatus;
        if (converged(hardStatus)) wrapper.apply_solution();
        wrapper.push_primitives_and_params(preferences);
    }
    return wrapper.solve(algorithm);
}

// A different native algorithm can converge on valid large edits that LM cannot.
// Retry the complete system from its original seed, never a failed partial solution.
export function solveNativeWithFallback(wrapper, primitives) {
    const baseline = structuredClone(primitives);
    let status = attempt(wrapper, primitives, Algorithm.LevenbergMarquardt);
    if (accepted(wrapper, status)) return status;

    restart(wrapper);
    status = attempt(wrapper, structuredClone(baseline), Algorithm.DogLeg);
    if (accepted(wrapper, status)) return status;

    // Only an explicitly edited or pending failed angle may select another sector.
    // One such angle has at most three alternatives; do not search combinations
    // or alter other stored dimensions to make a request converge.
    const branching = baseline.filter(primitive => primitive.type === "l2l_angle_ll" && primitive.temporary !== true
        && Array.isArray(primitive.alternativeAngles) && primitive.alternativeAngles.length);
    if (branching.length === 1) {
        const branch = branching[0];
        const visited = [branch.angle];
        for (const angle of branch.alternativeAngles.slice(0, 3)) {
            if (!Number.isFinite(angle) || visited.some(value =>
                Math.abs(Math.atan2(Math.sin(angle - value), Math.cos(angle - value))) < 1e-12)) continue;
            visited.push(angle);
            for (const algorithm of [Algorithm.LevenbergMarquardt, Algorithm.DogLeg]) {
                restart(wrapper);
                const candidate = structuredClone(baseline);
                candidate.find(primitive => primitive.id === branch.id).angle = angle;
                status = attempt(wrapper, candidate, algorithm);
                if (accepted(wrapper, status)) return status;
            }
        }
    }
    // The caller still checks conflicts, every geometric residual, fixed geometry,
    // and native dimension rank before publishing anything to the document.
    return status;
}
