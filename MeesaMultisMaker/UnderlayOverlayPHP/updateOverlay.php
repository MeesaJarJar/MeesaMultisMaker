<?php
/**
 * Overlay upload endpoint.
 *
 * POST /updateOverlay.php (multipart/form-data)
 * Required fields:
 *   - Facet/facet (int >= 0)
 *   - Left/left (int)
 *   - Top/top (int)
 *   - Width/width (int >= 1)
 *   - Height/height (int >= 1)
 *   - Image/image (PNG file)
 * Optional:
 *   - auth_token
 *   - source_path
 */

define('OVERLAYS_ROOT', __DIR__ . '/overlays/');
define('OVERLAYS_IMAGES_DIR', OVERLAYS_ROOT . 'images/');
define('OVERLAYS_INDEX_DIR', OVERLAYS_ROOT . 'index/');
define('AUTH_TOKEN', getenv('JARJAR_AUTH_TOKEN') ?: '');
define('MAX_OVERLAY_UPLOAD_BYTES', intval(getenv('OVERLAY_MAX_UPLOAD_BYTES') ?: (10 * 1024 * 1024)));

header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Methods: POST, OPTIONS');
header('Access-Control-Allow-Headers: Content-Type, Authorization');
header('Content-Type: application/json');

if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    http_response_code(200);
    exit;
}

if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    errorResponse(405, 'Method not allowed. Use POST.');
}

if (!empty(AUTH_TOKEN)) {
    $providedToken = getStringField(['auth_token']);
    if ($providedToken === null) {
        $providedToken = $_SERVER['HTTP_AUTHORIZATION'] ?? '';
        $providedToken = str_replace('Bearer ', '', $providedToken);
    }

    if ($providedToken !== AUTH_TOKEN) {
        errorResponse(403, 'Invalid authentication token');
    }
}

$facet = getIntField(['Facet', 'facet'], 'facet');
$left = getIntField(['Left', 'left'], 'left');
$top = getIntField(['Top', 'top'], 'top');
$width = getIntField(['Width', 'width'], 'width');
$height = getIntField(['Height', 'height'], 'height');
$sourcePath = getStringField(['source_path', 'SourcePath']);

if ($facet < 0) {
    errorResponse(400, 'Invalid facet');
}

if ($width < 1) {
    errorResponse(400, 'Invalid width');
}

if ($height < 1) {
    errorResponse(400, 'Invalid height');
}

$imageFieldName = isset($_FILES['Image']) ? 'Image' : (isset($_FILES['image']) ? 'image' : null);
if ($imageFieldName === null) {
    errorResponse(400, 'Missing required file: Image');
}

$file = $_FILES[$imageFieldName];
if (!isset($file['error']) || $file['error'] !== UPLOAD_ERR_OK) {
    $errorCode = isset($file['error']) ? intval($file['error']) : UPLOAD_ERR_NO_FILE;
    errorResponse(400, 'Image upload failed: ' . getUploadErrorMessage($errorCode));
}

if (!isset($file['size']) || intval($file['size']) < 1) {
    errorResponse(400, 'Image upload failed: Empty file');
}

if (intval($file['size']) > MAX_OVERLAY_UPLOAD_BYTES) {
    errorResponse(400, 'Image upload failed: File exceeds max size');
}

if (!is_uploaded_file($file['tmp_name'])) {
    errorResponse(400, 'Image upload failed: Invalid upload source');
}

if (!isPngUpload($file['tmp_name'], $file['type'] ?? '')) {
    errorResponse(400, 'Image upload failed: PNG only (image/png)');
}

$facetDir = OVERLAYS_IMAGES_DIR . $facet . '/';
if (!is_dir($facetDir) && !mkdir($facetDir, 0755, true)) {
    errorResponse(500, 'Failed to create overlay image directory');
}

if (!is_dir(OVERLAYS_INDEX_DIR) && !mkdir(OVERLAYS_INDEX_DIR, 0755, true)) {
    errorResponse(500, 'Failed to create overlay index directory');
}

$fileName = $left . '_' . $top . '_' . $width . '_' . $height . '.png';
$relativePath = '/overlays/images/' . $facet . '/' . $fileName;
$absolutePath = $facetDir . $fileName;

if (!move_uploaded_file($file['tmp_name'], $absolutePath)) {
    errorResponse(500, 'Failed to store uploaded image');
}

$newHash = hash_file('sha256', $absolutePath);
$updatedAt = gmdate('Y-m-d\TH:i:s\Z');
$overlayKey = $facet . ':' . $left . ':' . $top . ':' . $width . ':' . $height;

$record = [
    'Facet' => $facet,
    'Left' => $left,
    'Top' => $top,
    'Width' => $width,
    'Height' => $height,
    'Image' => $relativePath,
    'updated_at' => $updatedAt,
    'Hash' => $newHash,
];

if ($sourcePath !== null && $sourcePath !== '') {
    $record['source_path'] = $sourcePath;
}

$indexPath = OVERLAYS_INDEX_DIR . 'overlays_map' . $facet . '.json';
$indexData = loadFacetIndex($indexPath, $facet);
$indexData['records'][$overlayKey] = $record;
$indexData['updated_at'] = $updatedAt;

if (file_put_contents($indexPath, json_encode($indexData, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES), LOCK_EX) === false) {
    errorResponse(500, 'Failed to write facet overlay index');
}

sendJson(200, [
    'status' => 'ok',
    'message' => 'Overlay uploaded',
    'facet' => $facet,
    'left' => $left,
    'top' => $top,
    'width' => $width,
    'height' => $height,
    'path' => $relativePath,
    'new_hash' => $newHash,
]);

function loadFacetIndex($indexPath, $facet)
{
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

function isPngUpload($tmpPath, $reportedMime)
{
    if ($reportedMime !== '' && strtolower($reportedMime) !== 'image/png') {
        return false;
    }

    if (function_exists('finfo_open')) {
        $finfo = finfo_open(FILEINFO_MIME_TYPE);
        if ($finfo) {
            $mime = finfo_file($finfo, $tmpPath);
            finfo_close($finfo);
            if ($mime !== 'image/png') {
                return false;
            }
        }
    }

    $imageInfo = @getimagesize($tmpPath);
    if (!$imageInfo || !isset($imageInfo[2]) || intval($imageInfo[2]) !== IMAGETYPE_PNG) {
        return false;
    }

    return true;
}

function getIntField($keys, $label)
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

function getStringField($keys)
{
    foreach ($keys as $key) {
        if (isset($_POST[$key])) {
            return trim((string)$_POST[$key]);
        }
    }

    return null;
}

function getUploadErrorMessage($errorCode)
{
    $messages = [
        UPLOAD_ERR_INI_SIZE => 'File exceeds upload_max_filesize',
        UPLOAD_ERR_FORM_SIZE => 'File exceeds MAX_FILE_SIZE',
        UPLOAD_ERR_PARTIAL => 'File was only partially uploaded',
        UPLOAD_ERR_NO_FILE => 'No file was uploaded',
        UPLOAD_ERR_NO_TMP_DIR => 'Missing temporary folder',
        UPLOAD_ERR_CANT_WRITE => 'Failed to write file to disk',
        UPLOAD_ERR_EXTENSION => 'Upload stopped by extension',
    ];

    return $messages[$errorCode] ?? 'Unknown upload error';
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