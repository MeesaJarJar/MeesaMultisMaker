<?php
/**
 * Underlay read/write API.
 *
 * GET  /gameUnderlays.php?action=list&facet={facet}
 * GET  /gameUnderlays.php?action=get&facet={facet}&left={left}&top={top}&width={width}&height={height}
 * POST /gameUnderlays.php?action=delete  (multipart/form-data)
 *      Required fields: facet,left,top,width,height
 *      Optional: auth_token (or Authorization: Bearer ...)
 */

define('UNDERLAYS_ROOT', __DIR__ . '/underlays/');
define('UNDERLAYS_INDEX_DIR', UNDERLAYS_ROOT . 'index/');
define('AUTH_TOKEN', getenv('JARJAR_AUTH_TOKEN') ?: '');

header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Methods: GET, POST, OPTIONS');
header('Access-Control-Allow-Headers: Content-Type, Authorization');
header('Content-Type: application/json');

if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    http_response_code(200);
    exit;
}

$action = strtolower(trim(getRequestField(['action'], 'list')));

switch ($action) {
    case 'list':
        if ($_SERVER['REQUEST_METHOD'] !== 'GET') {
            errorResponse(405, 'Method not allowed for list. Use GET.');
        }
        handleList();
        break;

    case 'get':
        if ($_SERVER['REQUEST_METHOD'] !== 'GET') {
            errorResponse(405, 'Method not allowed for get. Use GET.');
        }
        handleGet();
        break;

    case 'delete':
        if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
            errorResponse(405, 'Method not allowed for delete. Use POST.');
        }
        handleDelete();
        break;

    default:
        errorResponse(400, 'Invalid action. Use list, get, or delete.');
}

function handleList()
{
    $facet = readIntQuery('facet');
    if ($facet < 0) {
        errorResponse(400, 'Invalid facet');
    }

    $indexData = loadFacetIndex($facet);
    $records = [];

    foreach ($indexData['records'] as $record) {
        if (!is_array($record)) {
            continue;
        }

        $records[] = [
            'facet'      => intval($record['Facet']      ?? $facet),
            'left'       => intval($record['Left']       ?? 0),
            'top'        => intval($record['Top']        ?? 0),
            'width'      => intval($record['Width']      ?? 0),
            'height'     => intval($record['Height']     ?? 0),
            'image'      => $record['Image']             ?? '',
            'hash'       => $record['Hash']              ?? '',
            'updated_at' => $record['updated_at']        ?? '',
        ];
    }

    sendJson(200, $records);
}

function handleGet()
{
    $facet = readIntQuery('facet');
    $left = readIntQuery('left');
    $top = readIntQuery('top');
    $width = readIntQuery('width');
    $height = readIntQuery('height');

    if ($facet < 0) {
        errorResponse(400, 'Invalid facet');
    }

    if ($width < 1) {
        errorResponse(400, 'Invalid width');
    }

    if ($height < 1) {
        errorResponse(400, 'Invalid height');
    }

    $key = buildUnderlayKey($facet, $left, $top, $width, $height);
    $indexData = loadFacetIndex($facet);

    if (!isset($indexData['records'][$key])) {
        errorResponse(404, 'Underlay not found');
    }

    $record = $indexData['records'][$key];
    $record['underlay_key'] = $key;

    sendJson(200, [
        'status' => 'ok',
        'record' => $record,
    ]);
}

function handleDelete()
{
    ensureAuthIfConfigured();

    $facet = readIntFromPost(['facet', 'Facet'], 'facet');
    $left = readIntFromPost(['left', 'Left'], 'left');
    $top = readIntFromPost(['top', 'Top'], 'top');
    $width = readIntFromPost(['width', 'Width'], 'width');
    $height = readIntFromPost(['height', 'Height'], 'height');

    if ($facet < 0) {
        errorResponse(400, 'Invalid facet');
    }

    if ($width < 1) {
        errorResponse(400, 'Invalid width');
    }

    if ($height < 1) {
        errorResponse(400, 'Invalid height');
    }

    $key = buildUnderlayKey($facet, $left, $top, $width, $height);
    $indexData = loadFacetIndex($facet);

    if (!isset($indexData['records'][$key])) {
        sendJson(200, [
            'status' => 'ok',
            'message' => 'Underlay already absent',
            'facet' => $facet,
            'left' => $left,
            'top' => $top,
            'width' => $width,
            'height' => $height,
            'underlay_key' => $key,
            'file_deleted' => false,
            'already_absent' => true,
        ]);
    }

    $record = $indexData['records'][$key];
    $imagePath = $record['Image'] ?? '';

    unset($indexData['records'][$key]);
    $indexData['updated_at'] = gmdate('Y-m-d\TH:i:s\Z');

    if (!is_dir(UNDERLAYS_INDEX_DIR) && !mkdir(UNDERLAYS_INDEX_DIR, 0755, true)) {
        errorResponse(500, 'Failed to create underlay index directory');
    }

    $indexPath = UNDERLAYS_INDEX_DIR . 'underlays_map' . $facet . '.json';
    if (file_put_contents($indexPath, json_encode($indexData, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES), LOCK_EX) === false) {
        errorResponse(500, 'Failed to update underlay index');
    }

    $fileDeleted = false;
    if ($imagePath !== '') {
        $absolutePath = resolveUnderlayImagePath($imagePath);
        if ($absolutePath !== null && file_exists($absolutePath)) {
            $fileDeleted = @unlink($absolutePath);
        }
    }

    sendJson(200, [
        'status' => 'ok',
        'message' => 'Underlay removed',
        'facet' => $facet,
        'left' => $left,
        'top' => $top,
        'width' => $width,
        'height' => $height,
        'underlay_key' => $key,
        'file_deleted' => $fileDeleted,
    ]);
}

function buildUnderlayKey($facet, $left, $top, $width, $height)
{
    return $facet . ':' . $left . ':' . $top . ':' . $width . ':' . $height;
}

function loadFacetIndex($facet)
{
    $indexPath = UNDERLAYS_INDEX_DIR . 'underlays_map' . $facet . '.json';

    $default = [
        'facet' => $facet,
        'updated_at' => gmdate('Y-m-d\TH:i:s\Z'),
        'records' => []
    ];

    if (!file_exists($indexPath)) {
        return $default;
    }

    $json = file_get_contents($indexPath);
    if ($json === false || trim($json) === '') {
        return $default;
    }

    $decoded = json_decode($json, true);
    if (!is_array($decoded)) {
        return $default;
    }

    if (isset($decoded['records']) && is_array($decoded['records'])) {
        $default['records'] = $decoded['records'];
        if (isset($decoded['updated_at'])) {
            $default['updated_at'] = $decoded['updated_at'];
        }
        return $default;
    }

    if (array_keys($decoded) === range(0, count($decoded) - 1)) {
        foreach ($decoded as $row) {
            if (!is_array($row)) {
                continue;
            }

            $k = ($row['Facet'] ?? $facet) . ':' . ($row['Left'] ?? 0) . ':' . ($row['Top'] ?? 0) . ':' . ($row['Width'] ?? 0) . ':' . ($row['Height'] ?? 0);
            $default['records'][$k] = $row;
        }
    }

    return $default;
}

function ensureAuthIfConfigured()
{
    if (empty(AUTH_TOKEN)) {
        return;
    }

    $providedToken = getRequestField(['auth_token']);
    if ($providedToken === null) {
        $providedToken = $_SERVER['HTTP_AUTHORIZATION'] ?? '';
        $providedToken = str_replace('Bearer ', '', $providedToken);
    }

    if ($providedToken !== AUTH_TOKEN) {
        errorResponse(403, 'Invalid authentication token');
    }
}

function resolveUnderlayImagePath($relativePath)
{
    if ($relativePath === null || trim($relativePath) === '') {
        return null;
    }

    $candidate = __DIR__ . '/' . ltrim(str_replace(['\\', '..'], ['/', ''], $relativePath), '/');
    $realRoot = realpath(UNDERLAYS_ROOT);
    $realFile = realpath($candidate);

    // File may already be gone. In that case, validate by normalized prefix.
    if ($realFile === false) {
        $normalized = str_replace('\\', '/', $candidate);
        $rootNorm = str_replace('\\', '/', UNDERLAYS_ROOT);
        if (strpos($normalized, $rootNorm) !== 0) {
            return null;
        }
        return $candidate;
    }

    if ($realRoot === false) {
        return null;
    }

    $rootNorm = str_replace('\\', '/', $realRoot);
    $fileNorm = str_replace('\\', '/', $realFile);
    if (strpos($fileNorm, $rootNorm) !== 0) {
        return null;
    }

    return $realFile;
}

function getRequestField($keys, $default = null)
{
    foreach ($keys as $key) {
        if (isset($_POST[$key])) {
            return trim((string)$_POST[$key]);
        }
        if (isset($_GET[$key])) {
            return trim((string)$_GET[$key]);
        }
    }

    return $default;
}

function readIntFromPost($keys, $label)
{
    foreach ($keys as $key) {
        if (isset($_POST[$key])) {
            if (filter_var($_POST[$key], FILTER_VALIDATE_INT) === false) {
                errorResponse(400, 'Invalid ' . $label);
            }
            return intval($_POST[$key]);
        }
    }

    errorResponse(400, 'Missing required parameter: ' . $label);
}

function readIntQuery($name)
{
    if (!isset($_GET[$name]) || filter_var($_GET[$name], FILTER_VALIDATE_INT) === false) {
        errorResponse(400, 'Missing or invalid parameter: ' . $name);
    }

    return intval($_GET[$name]);
}

function errorResponse($statusCode, $message)
{
    sendJson($statusCode, [
        'status' => 'error',
        'message' => $message,
        'Message' => $message,
    ]);
}

function sendJson($statusCode, $payload)
{
    http_response_code($statusCode);
    echo json_encode($payload, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES);
    exit;
}
?>